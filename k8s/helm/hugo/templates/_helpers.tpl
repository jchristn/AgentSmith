{{/* Namespace of every object. */}}
{{- define "hugo.namespace" -}}
{{- default .Release.Namespace .Values.namespaceOverride -}}
{{- end -}}

{{/* Image reference: registry/name:tag. */}}
{{- define "hugo.image" -}}
{{- printf "%s/%s:%s" .root.Values.image.registry .name .root.Values.image.tag -}}
{{- end -}}

{{/* Labels on every object (the Kustomize base adds app.kubernetes.io/part-of: hugo to every object). */}}
{{- define "hugo.labels" -}}
app.kubernetes.io/part-of: hugo
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ .Chart.Name }}-{{ .Chart.Version | replace "+" "_" }}
{{- end -}}

{{/* Topology spread constraints with a default labelSelector for the named app. */}}
{{- define "hugo.topologySpread" -}}
{{- $app := .app -}}
{{- range .constraints }}
- maxSkew: {{ .maxSkew }}
  topologyKey: {{ .topologyKey }}
  whenUnsatisfiable: {{ .whenUnsatisfiable }}
  labelSelector:
    {{- if .labelSelector }}
    {{- toYaml .labelSelector | nindent 4 }}
    {{- else }}
    matchLabels:
      app.kubernetes.io/name: {{ $app }}
    {{- end }}
{{- end }}
{{- end -}}

{{/* Restricted container security context. */}}
{{- define "hugo.restrictedContainer" -}}
allowPrivilegeEscalation: false
readOnlyRootFilesystem: true
capabilities:
  drop: ["ALL"]
{{- end -}}

{{/* Kubelet HTTP or TCP probes for third-party images: startup (five minutes), readiness and liveness (5 s, 2). */}}
{{- define "hugo.httpProbes" -}}
startupProbe:
  httpGet:
    path: {{ .ready }}
    port: http
  periodSeconds: 5
  timeoutSeconds: 4
  failureThreshold: 60
readinessProbe:
  httpGet:
    path: {{ .ready }}
    port: http
  periodSeconds: 5
  timeoutSeconds: 4
  failureThreshold: 2
livenessProbe:
  {{- if .live }}
  httpGet:
    path: {{ .live }}
    port: http
  {{- else }}
  tcpSocket:
    port: http
  {{- end }}
  periodSeconds: 5
  timeoutSeconds: 4
  failureThreshold: 2
{{- end -}}

{{/* PersistentVolumeClaim. */}}
{{- define "hugo.pvc" -}}
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: {{ .name }}
  namespace: {{ include "hugo.namespace" .root }}
  labels:
    {{- include "hugo.labels" .root | nindent 4 }}
    app.kubernetes.io/name: {{ .app }}
spec:
  accessModes: ["ReadWriteOnce"]
  {{- if .storageClassName }}
  storageClassName: {{ .storageClassName }}
  {{- end }}
  resources:
    requests:
      storage: {{ .storage }}
{{- end -}}

{{/* The wait-for-database init container: pg_isready until PostgreSQL accepts connections. */}}
{{- define "hugo.waitForDatabase" -}}
{{- $db := .Values.database -}}
- name: wait-for-database
  image: {{ $db.image }}
  imagePullPolicy: IfNotPresent
  command:
    - sh
    - -c
    - |
      echo "waiting for PostgreSQL at ${HUGO_DB_HOST}:${HUGO_DB_PORT}"
      {{- if eq $db.mode "external" }}
      until pg_isready -h "${HUGO_DB_HOST}" -p "${HUGO_DB_PORT}" -U "${HUGO_DB_USERNAME}" -d "${HUGO_DB_DATABASE}"; do sleep 2; done
      {{- else }}
      until pg_isready -h "${HUGO_DB_HOST}" -p "${HUGO_DB_PORT}" -U hugo -d hugo; do sleep 2; done
      {{- end }}
      echo "PostgreSQL is ready"
  env:
    {{- if eq $db.mode "external" }}
    {{- range $pair := list (list "HUGO_DB_HOST" "host") (list "HUGO_DB_PORT" "port") (list "HUGO_DB_DATABASE" "database") (list "HUGO_DB_USERNAME" "username") }}
    - name: {{ index $pair 0 }}
      valueFrom:
        secretKeyRef:
          name: {{ $db.external.secretName }}
          key: {{ index $pair 1 }}
    {{- end }}
    {{- else }}
    - name: HUGO_DB_HOST
      value: hugo-postgres
    - name: HUGO_DB_PORT
      value: "5432"
    {{- end }}
  resources:
    requests:
      cpu: 10m
      memory: 16Mi
    limits:
      cpu: 100m
      memory: 64Mi
  securityContext:
    runAsUser: 70
    runAsGroup: 70
    {{- include "hugo.restrictedContainer" . | nindent 4 }}
{{- end -}}

{{/* Database environment for the server container. */}}
{{- define "hugo.databaseEnv" -}}
{{- $db := .Values.database -}}
{{- if eq $db.mode "external" }}
- name: HUGO_DB_TYPE
  value: Postgresql
{{- range $pair := list (list "HUGO_DB_HOST" "host") (list "HUGO_DB_PORT" "port") (list "HUGO_DB_DATABASE" "database") (list "HUGO_DB_USERNAME" "username") (list "HUGO_DB_PASSWORD" "password") }}
- name: {{ index $pair 0 }}
  valueFrom:
    secretKeyRef:
      name: {{ $db.external.secretName }}
      key: {{ index $pair 1 }}
{{- end }}
{{- else if eq $db.mode "embedded" }}
- name: HUGO_DB_TYPE
  value: Postgresql
- name: HUGO_DB_HOST
  value: hugo-postgres
- name: HUGO_DB_PORT
  value: "5432"
- name: HUGO_DB_DATABASE
  value: hugo
- name: HUGO_DB_USERNAME
  value: hugo
- name: HUGO_DB_PASSWORD
  valueFrom:
    secretKeyRef:
      name: {{ .Values.secrets.name }}
      key: db-password
{{- else if ne $db.mode "sqlite" }}
{{- fail (printf "database.mode must be sqlite, embedded, or external (got %q)" $db.mode) }}
{{- end }}
{{- end -}}

{{/* Observability environment for the server container (components/observability/server-telemetry.yaml). */}}
{{- define "hugo.observabilityEnv" -}}
{{- if .Values.observability.enabled }}
{{- $u := .Values.observability.urls }}
- name: HUGO_OTLP_ENDPOINT
  value: http://hugo-alloy:4317
- name: HUGO_GRAFANA_INTERNAL_URL
  value: http://hugo-grafana:3000
- name: HUGO_PROMETHEUS_INTERNAL_URL
  value: http://hugo-prometheus:9090
- name: HUGO_TEMPO_INTERNAL_URL
  value: http://hugo-tempo:3200
- name: HUGO_LOKI_INTERNAL_URL
  value: http://hugo-loki:3100
- name: HUGO_GRAFANA_URL
  value: {{ $u.grafana }}
- name: HUGO_PROMETHEUS_URL
  value: {{ $u.prometheus }}
- name: HUGO_TEMPO_URL
  value: {{ $u.tempo }}
- name: HUGO_LOKI_URL
  value: {{ $u.loki }}
{{- end }}
{{- end -}}
