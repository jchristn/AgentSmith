# Kubernetes RBAC for Hugo

Hugo's access modes, namespace allow list, and classifiers decide what an agent may attempt. The cluster's RBAC decides what the credential can actually do. Treat RBAC as the first line of defense and Hugo as the second: give each Kubernetes resource a service account that can do no more than the bindings on it need.

This guide has two levels:

- **Read-only** covers `k8s_list`, `k8s_get`, `k8s_describe`, `k8s_events`, `k8s_logs`, `k8s_top`, `k8s_rollout_status`, and `k8s_rollout_history`. Use it for resources that are only ever bound ReadOnly.
- **Operator** adds `k8s_rollout_restart`, `k8s_scale`, `k8s_apply`, `k8s_delete`, and `k8s_exec` on workloads. Use it for resources bound ReadWriteWithApproval or ReadWrite.

Each level comes as a namespaced `Role` (bind it once per allowed namespace) and a small `ClusterRole` for the cluster-scoped reads that `k8s_describe`, `k8s_top`, and node listing use. Leave the cluster-scoped part out when the resource sets `clusterScopedReads` to false.

## Secrets

Hugo never returns Secret values: `k8s_get` and `k8s_describe` redact them, and `k8s_list` shows names and types only. Even so, the safest credential cannot read Secrets at all, so neither Role below grants `get` or `list` on `secrets`. Add it only if operators need to list Secret names, and prefer `list` without `get`.

`k8s_apply` of a Secret needs `create` and `patch` on `secrets`. The operator Role grants them commented out; enable them deliberately.

## Read-only

```yaml
# Namespaced read access. Bind once per namespace in the resource's allowedNamespaces.
apiVersion: rbac.authorization.k8s.io/v1
kind: Role
metadata:
  name: hugo-read-only
  namespace: shop
rules:
  - apiGroups: [""]
    resources: ["pods", "pods/log", "services", "endpoints", "configmaps", "events", "persistentvolumeclaims", "replicationcontrollers", "serviceaccounts"]
    verbs: ["get", "list", "watch"]
  - apiGroups: ["apps"]
    resources: ["deployments", "replicasets", "statefulsets", "daemonsets", "controllerrevisions"]
    verbs: ["get", "list", "watch"]
  - apiGroups: ["batch"]
    resources: ["jobs", "cronjobs"]
    verbs: ["get", "list", "watch"]
  - apiGroups: ["autoscaling"]
    resources: ["horizontalpodautoscalers"]
    verbs: ["get", "list", "watch"]
  - apiGroups: ["networking.k8s.io"]
    resources: ["ingresses", "networkpolicies"]
    verbs: ["get", "list", "watch"]
  - apiGroups: ["events.k8s.io"]
    resources: ["events"]
    verbs: ["get", "list", "watch"]
  - apiGroups: ["metrics.k8s.io"]
    resources: ["pods"]
    verbs: ["get", "list"]
---
apiVersion: rbac.authorization.k8s.io/v1
kind: RoleBinding
metadata:
  name: hugo-read-only
  namespace: shop
subjects:
  - kind: ServiceAccount
    name: hugo
    namespace: hugo-system
roleRef:
  apiGroup: rbac.authorization.k8s.io
  kind: Role
  name: hugo-read-only
---
# Cluster-scoped reads: nodes for k8s_describe and k8s_top, namespaces for k8s_list, and CRDs so custom
# resources show up. Omit this when the resource sets clusterScopedReads to false.
apiVersion: rbac.authorization.k8s.io/v1
kind: ClusterRole
metadata:
  name: hugo-cluster-read
rules:
  - apiGroups: [""]
    resources: ["nodes", "namespaces"]
    verbs: ["get", "list", "watch"]
  - apiGroups: ["apiextensions.k8s.io"]
    resources: ["customresourcedefinitions"]
    verbs: ["get", "list", "watch"]
  - apiGroups: ["metrics.k8s.io"]
    resources: ["nodes"]
    verbs: ["get", "list"]
---
apiVersion: rbac.authorization.k8s.io/v1
kind: ClusterRoleBinding
metadata:
  name: hugo-cluster-read
subjects:
  - kind: ServiceAccount
    name: hugo
    namespace: hugo-system
roleRef:
  apiGroup: rbac.authorization.k8s.io
  kind: ClusterRole
  name: hugo-cluster-read
```

Discovery (`/api` and `/apis`) is readable by every authenticated user through the built-in `system:discovery` role, so Hugo can resolve kinds without extra rules.

To read custom resources in a namespace, add a rule per API group, for example:

```yaml
  - apiGroups: ["example.com"]
    resources: ["widgets"]
    verbs: ["get", "list", "watch"]
```

## Operator

```yaml
# Everything in hugo-read-only, plus the changes the Write and Destructive tools make.
apiVersion: rbac.authorization.k8s.io/v1
kind: Role
metadata:
  name: hugo-operator
  namespace: shop
rules:
  - apiGroups: [""]
    resources: ["pods", "pods/log", "services", "endpoints", "configmaps", "events", "persistentvolumeclaims", "replicationcontrollers", "serviceaccounts"]
    verbs: ["get", "list", "watch"]
  - apiGroups: ["apps"]
    resources: ["deployments", "replicasets", "statefulsets", "daemonsets", "controllerrevisions"]
    verbs: ["get", "list", "watch"]
  - apiGroups: ["batch"]
    resources: ["jobs", "cronjobs"]
    verbs: ["get", "list", "watch"]
  - apiGroups: ["events.k8s.io"]
    resources: ["events"]
    verbs: ["get", "list", "watch"]
  - apiGroups: ["metrics.k8s.io"]
    resources: ["pods"]
    verbs: ["get", "list"]
  # k8s_rollout_restart, k8s_apply, k8s_delete on workloads.
  - apiGroups: ["apps"]
    resources: ["deployments", "statefulsets", "daemonsets"]
    verbs: ["create", "patch", "update", "delete"]
  # k8s_scale.
  - apiGroups: ["apps"]
    resources: ["deployments/scale", "statefulsets/scale", "replicasets/scale"]
    verbs: ["get", "patch", "update"]
  # k8s_apply and k8s_delete on services, config maps, and jobs.
  - apiGroups: [""]
    resources: ["services", "configmaps"]
    verbs: ["create", "patch", "update", "delete"]
  - apiGroups: ["batch"]
    resources: ["jobs", "cronjobs"]
    verbs: ["create", "patch", "update", "delete"]
  # k8s_delete of pods (prefer k8s_rollout_restart) and k8s_exec.
  - apiGroups: [""]
    resources: ["pods"]
    verbs: ["delete"]
  - apiGroups: [""]
    resources: ["pods/exec"]
    verbs: ["create", "get"]
  # Applying Secrets. Enable deliberately; Hugo still never returns Secret values.
  # - apiGroups: [""]
  #   resources: ["secrets"]
  #   verbs: ["create", "patch", "update"]
---
apiVersion: rbac.authorization.k8s.io/v1
kind: RoleBinding
metadata:
  name: hugo-operator
  namespace: shop
subjects:
  - kind: ServiceAccount
    name: hugo
    namespace: hugo-system
roleRef:
  apiGroup: rbac.authorization.k8s.io
  kind: Role
  name: hugo-operator
---
# k8s_cordon. Only bind this when the resource sets allowClusterScoped to true.
apiVersion: rbac.authorization.k8s.io/v1
kind: ClusterRole
metadata:
  name: hugo-node-operator
rules:
  - apiGroups: [""]
    resources: ["nodes"]
    verbs: ["get", "list", "watch", "patch"]
```

`pods/exec` is the most powerful verb in this list: it runs arbitrary commands with the container's identity, including reading mounted Secrets and service account tokens. Hugo classifies every `k8s_exec` call as Destructive, but leave `pods/exec` out of the Role unless operators need it.

The operator Role deliberately omits RBAC objects, CRDs, namespaces, persistent volumes, storage classes, and webhooks. Hugo classifies applies of those kinds as Destructive (the `destructiveKinds` setting), and cluster-scoped kinds also need `allowClusterScoped`. Grant them, if ever, from a separate, tightly bound role.

## A service account token for Hugo

On Kubernetes 1.24 and later, service accounts get no long-lived token automatically. Create one explicitly and bind it to the resource's `serviceAccountToken` slot, with the cluster CA in the `certificateAuthority` slot and the API server URL in `serverUrl`:

```yaml
apiVersion: v1
kind: ServiceAccount
metadata:
  name: hugo
  namespace: hugo-system
---
apiVersion: v1
kind: Secret
metadata:
  name: hugo-token
  namespace: hugo-system
  annotations:
    kubernetes.io/service-account.name: hugo
type: kubernetes.io/service-account-token
```

```bash
kubectl -n hugo-system get secret hugo-token -o jsonpath='{.data.token}' | base64 -d     # serviceAccountToken slot
kubectl -n hugo-system get secret hugo-token -o jsonpath='{.data.ca\.crt}' | base64 -d   # certificateAuthority slot
```

Kubeconfigs work too, as long as their credentials are inline (`token`, or `client-certificate-data` and `client-key-data`). Hugo refuses kubeconfigs that use `exec` or `auth-provider` plugins or reference files, because those would run programs or read files on the Hugo server.

The k3s testbed (`docker/testbed/kubernetes`) creates the `hugo-reader` service account with the read-only Role above, and the `testbed.kubernetes` suite checks that it can list pods but not Secrets.
