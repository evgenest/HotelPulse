# Data Persistence in Kubernetes — A Practical Guide

Using the HotelPulse project as a concrete example.

---

## How Docker Named Volumes Differ from Kubernetes PVCs

With Docker Compose, a Named Volume lives directly on the host machine:

```
C:\Users\<user>\AppData\Local\Docker\volumes\...
```

It **survives** `docker compose down` — data remains until you explicitly pass the `-v` flag:

```bash
docker compose down       # containers removed, data intact
docker compose down -v    # containers removed, data gone too
```

Kubernetes works differently. Data is stored not on the host, but **inside the cluster node**.

---

## How Storage Is Configured in This Project

MongoDB in [`k8s/mongo.yaml`](../k8s/mongo.yaml) is defined as a StatefulSet with `volumeClaimTemplates`:

```yaml
volumeClaimTemplates:
  - metadata:
      name: data
    spec:
      accessModes: [ReadWriteOnce]
      resources:
        requests:
          storage: 2Gi
```

When you apply this manifest, Kubernetes automatically:

1. Creates a **PersistentVolumeClaim (PVC)** — a request for storage
2. Provisions a **PersistentVolume (PV)** — the actual "virtual disk"
3. Mounts the PV into the pod at `/data/db`

---

## Where Data Actually Lives — kind vs minikube

| Tool | Where the PV is stored |
|---|---|
| **kind** | Inside the Docker container that acts as the cluster node |
| **minikube** | Inside the minikube virtual machine |

In both cases the data does **not** reside directly on your host as a folder or Docker volume.

---

## What Happens When You Delete Things

### `kubectl delete namespace hotelpulse`

Deletes everything inside the namespace: pods, services, PVCs. Since dynamically provisioned volumes default to `reclaimPolicy: Delete`, the PV is deleted along with the PVC — MongoDB data is gone.

### `kind delete cluster` / `minikube delete`

Destroys the node itself (the Docker container or VM). All storage that lived inside is **permanently gone** — even if you skipped the namespace deletion step.

### Summary table

| Action | MongoDB data |
|---|---|
| `kubectl delete namespace hotelpulse` | Deleted (`Delete` reclaim policy) |
| `kind delete cluster` / `minikube delete` | Permanently gone |
| `docker compose down` (no `-v`) | **Intact** |
| `docker compose down -v` | Deleted |

---

## What Happens When You Recreate the Cluster

After `kind create cluster` / `minikube start` + `kubectl apply -f k8s/` you start with a **clean slate**:

- MongoDB is empty
- All booking data is gone
- **Hotels will reappear automatically** — the API seeds them on startup (see `apps/api/Program.cs`)

This behaviour is documented in the manifest itself [`k8s/mongo.yaml`](../k8s/mongo.yaml):

```
the API reseeds hotels on startup
```

---

## How to Persist Data Across Cluster Recreation (for reference)

In production you use **external volumes** that exist independently of the cluster:

| Provider | Volume type |
|---|---|
| AWS | EBS (Elastic Block Store) |
| Google Cloud | Persistent Disk |
| Azure | Azure Disk |
| Any | NFS, Ceph, Longhorn |

For local development you can use `hostPath` — mounting a host directory directly into the pod:

```yaml
volumes:
  - name: data
    hostPath:
      path: /tmp/hotelpulse-mongo   # directory on the host
      type: DirectoryOrCreate
```

This makes data survive `kubectl delete namespace`. However it does **not** survive `kind delete cluster` — the node and its filesystem are still destroyed.

> For this learning project `hostPath` is unnecessary — the API seeds data automatically.

---

## Key Takeaway

- **Docker Named Volume**: lives on the host → survives `down`
- **k8s PVC with kind/minikube**: lives inside the node → dies with the cluster
- **k8s PVC in the cloud**: lives on an external disk → survives cluster recreation

Deleting a local cluster always gives you a clean slate. For HotelPulse that's perfectly fine: hotels come back on their own, and bookings are demo data.
