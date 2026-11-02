# Хранение данных в Kubernetes — практическое объяснение

На примере проекта HotelPulse.

---

## Как Docker Named Volumes отличаются от Kubernetes PVC

Когда работаешь с Docker Compose, Named Volume живёт прямо на хосте:

```
C:\Users\<user>\AppData\Local\Docker\volumes\...
```

Он **переживает** `docker compose down` — данные остаются, пока ты явно не передашь флаг `-v`:

```bash
docker compose down       # контейнеры удалены, данные целы
docker compose down -v    # контейнеры удалены, данные тоже
```

В Kubernetes всё по-другому. Данные хранятся не на хосте, а **внутри ноды кластера**.

---

## Как устроено хранилище в этом проекте

PostgreSQL в [`k8s/postgres.yaml`](../k8s/postgres.yaml) описана как StatefulSet с `volumeClaimTemplates`:

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

Когда ты применяешь этот манифест, Kubernetes автоматически:

1. Создаёт **PersistentVolumeClaim (PVC)** — запрос на хранилище
2. Под него создаётся **PersistentVolume (PV)** — сам "виртуальный диск"
3. PV монтируется в под по пути `/var/lib/postgresql/data`

---

## Где физически живут данные — kind vs minikube

| Инструмент | Где хранится PV |
|---|---|
| **kind** | Внутри Docker-контейнера, который является нодой кластера |
| **minikube** | Внутри виртуальной машины minikube |

В обоих случаях данные **не лежат** напрямую на твоём хосте в виде папки или Docker Volume.

---

## Что происходит при удалении

### `kubectl delete namespace hotelpulse`

Удаляет всё внутри неймспейса: поды, сервисы, PVC. Поскольку у динамически созданных томов по умолчанию стоит `reclaimPolicy: Delete`, вместе с PVC удаляется и PV — данные PostgreSQL стираются.

### `kind delete cluster` / `minikube delete`

Уничтожает саму ноду (Docker-контейнер или VM). Всё хранилище, которое было внутри, **гарантированно исчезает** — даже если ты пропустил шаг с удалением неймспейса.

### Итоговая таблица

| Действие | Данные PostgreSQL |
|---|---|
| `kubectl delete namespace hotelpulse` | Удалены (политика `Delete`) |
| `kind delete cluster` / `minikube delete` | Удалены безвозвратно |
| `docker compose down` (без `-v`) | **Целы** |
| `docker compose down -v` | Удалены |

---

## Что происходит при повторном подъёме кластера

При `kind create cluster` / `minikube start` + `kubectl apply -f k8s/` ты получаешь **чистый лист**:

- PostgreSQL пустая
- Данные бронирований отсутствуют
- **Отели появятся автоматически** — миграция `002_seed.sql` вставляет каталог при старте API

Redis не хранит постоянные данные: это кэш, и после пересоздания он пуст.

---

## Как сделать персистентность между пересозданиями (для справки)

В продакшне используют **внешние тома**, которые существуют независимо от кластера:

| Провайдер | Тип тома |
|---|---|
| AWS | EBS (Elastic Block Store) |
| Google Cloud | Persistent Disk |
| Azure | Azure Disk |
| Любой | NFS, Ceph, Longhorn |

Для локальной разработки можно использовать `hostPath` — монтирование папки с хоста прямо в под:

```yaml
volumes:
  - name: data
    hostPath:
      path: /tmp/hotelpulse-postgres   # папка на хосте
      type: DirectoryOrCreate
```

Тогда данные выживут даже после `kubectl delete namespace`. Но **не после** `kind delete cluster` — нода с её файловой системой всё равно исчезнет.

> `hostPath` здесь не нужен — каталог вставляют SQL-миграции.

---

## Краткий вывод

- **Docker Named Volume**: живёт на хосте → переживает `down`
- **k8s PVC в kind/minikube**: живёт внутри ноды → умирает вместе с кластером
- **k8s PVC в облаке**: живёт на внешнем диске → переживает пересоздание кластера

Удаление кластера в локальной разработке — это всегда чистый лист. Для HotelPulse это нормально: отели возвращаются сами, бронирования — демо-данные.
