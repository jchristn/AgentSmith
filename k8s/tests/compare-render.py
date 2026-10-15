#!/usr/bin/env python3
"""Compare the objects Kustomize and Helm render for Hugo.

Usage: compare-render.py <kustomize.yaml> <helm.yaml>

Both files are multi-document YAML. They are converted to JSON with PyYAML when it is installed, otherwise with yq
(mikefarah yq v4, preinstalled on GitHub runners). Objects are matched by kind, namespace, and name and must be equal
after these normalizations, which remove differences that do not change what runs:

  - the Namespace object is ignored (Helm leaves namespaces to --create-namespace or generate-secrets);
  - the Helm bookkeeping labels app.kubernetes.io/managed-by and helm.sh/chart are dropped;
  - pod template annotations named checksum/* are dropped (Helm's restart-on-config-change; Kustomize renames
    the generated ConfigMap instead);
  - the hash suffix Kustomize appends to generated ConfigMap names is removed, in names and in references;
  - ConfigMap data values are compared without trailing newlines;
  - lists whose items all carry a "name" (containers, env, ports, volumes, volume mounts) are compared as sets
    ordered by name, because patches append entries in a different order than templates write them.

Exit code 0 when the object sets are equal, 1 otherwise (each difference is printed).
"""

import json
import re
import shutil
import subprocess
import sys


def load(path):
    try:
        import yaml  # type: ignore

        with open(path, encoding="utf-8") as fh:
            return [d for d in yaml.safe_load_all(fh) if d]
    except ImportError:
        pass
    if not shutil.which("yq"):
        sys.exit("compare-render: needs PyYAML or yq (https://github.com/mikefarah/yq) on PATH")
    out = subprocess.run(["yq", "-o=json", "-I=0", "ea", "[.] | .[] | select(. != null)", path],
                         check=True, capture_output=True, text=True).stdout
    docs = []
    decoder = json.JSONDecoder()
    text = out.strip()
    i = 0
    while i < len(text):
        while i < len(text) and text[i].isspace():
            i += 1
        if i >= len(text):
            break
        obj, i = decoder.raw_decode(text, i)
        docs.append(obj)
    return docs


HASHED = re.compile(r"^(.+)-[a-z0-9]{10}$")


def generated_names(docs):
    names = {}
    for d in docs:
        if d.get("kind") == "ConfigMap":
            name = d["metadata"]["name"]
            m = HASHED.match(name)
            if m:
                names[name] = m.group(1)
    return names


def rename(node, names):
    if isinstance(node, dict):
        return {k: rename(v, names) for k, v in node.items()}
    if isinstance(node, list):
        return [rename(v, names) for v in node]
    if isinstance(node, str) and node in names:
        return names[node]
    return node


def normalize(node):
    if isinstance(node, dict):
        out = {}
        for k, v in node.items():
            out[k] = normalize(v)
        return out
    if isinstance(node, list):
        items = [normalize(v) for v in node]
        if items and all(isinstance(v, dict) and "name" in v for v in items):
            items.sort(key=lambda v: str(v["name"]))
        return items
    return node


def clean(doc):
    meta = doc.setdefault("metadata", {})
    labels = meta.get("labels") or {}
    for key in ("app.kubernetes.io/managed-by", "helm.sh/chart"):
        labels.pop(key, None)
    if not labels:
        meta.pop("labels", None)
    template = (doc.get("spec") or {}).get("template") or {}
    tmeta = template.get("metadata") or {}
    ann = tmeta.get("annotations")
    if ann:
        for key in [k for k in ann if k.startswith("checksum/")]:
            del ann[key]
        if not ann:
            tmeta.pop("annotations")
    if doc.get("kind") == "ConfigMap" and isinstance(doc.get("data"), dict):
        doc["data"] = {k: (v.rstrip("\n") if isinstance(v, str) else v) for k, v in doc["data"].items()}
    return normalize(doc)


def index(docs):
    names = generated_names(docs)
    out = {}
    for d in docs:
        if d.get("kind") == "Namespace":
            continue
        d = clean(rename(d, names))
        key = "%s/%s/%s" % (d.get("kind"), d["metadata"].get("namespace", ""), d["metadata"]["name"])
        if key in out:
            print("duplicate object %s" % key)
        out[key] = d
    return out


def diff(a, b, path, problems):
    if type(a) is not type(b):
        problems.append("%s: %r != %r" % (path, a, b))
    elif isinstance(a, dict):
        for k in sorted(set(a) | set(b)):
            if k not in a:
                problems.append("%s.%s: only in helm" % (path, k))
            elif k not in b:
                problems.append("%s.%s: only in kustomize" % (path, k))
            else:
                diff(a[k], b[k], "%s.%s" % (path, k), problems)
    elif isinstance(a, list):
        if len(a) != len(b):
            problems.append("%s: %d items in kustomize, %d in helm" % (path, len(a), len(b)))
        for i, (x, y) in enumerate(zip(a, b)):
            diff(x, y, "%s[%d]" % (path, i), problems)
    elif a != b:
        problems.append("%s: kustomize %r != helm %r" % (path, a, b))


def main():
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    kustomize = index(load(sys.argv[1]))
    helm = index(load(sys.argv[2]))
    problems = []
    for key in sorted(set(kustomize) | set(helm)):
        if key not in helm:
            problems.append("%s: only in kustomize" % key)
        elif key not in kustomize:
            problems.append("%s: only in helm" % key)
        else:
            diff(kustomize[key], helm[key], key, problems)
    if problems:
        for p in problems:
            print("DIFF " + p)
        print("compare-render: %s and %s differ (%d differences)" % (sys.argv[1], sys.argv[2], len(problems)))
        sys.exit(1)
    print("compare-render: %d objects equal (%s, %s)" % (len(kustomize), sys.argv[1], sys.argv[2]))


if __name__ == "__main__":
    main()
