# k8s_eso_ansible_app

Automatyzacja deploymentu infrastruktury Kubernetes z integracją HashiCorp Vault, External Secrets Operator, ArgoCD i Apache Kafka -- orkiestrowana przez Ansible.

## Architektura

```
Vault (Docker)                ArgoCD (GitOps)
    │                              │
    │  K8s Auth + JWT              │  auto-sync manifests
    ▼                              ▼
External Secrets Operator ──► K8s Secret ──► Nginx (demo-app)
                                               │
Strimzi Kafka ◄── .NET Producer/Consumer ──────┘
```

## Stack technologiczny

| Warstwa | Technologia |
|---------|-------------|
| Orkiestracja | Ansible (6 playbooków + master) |
| Klaster K8s | Minikube + Docker |
| Sekrety | HashiCorp Vault + External Secrets Operator |
| GitOps | ArgoCD |
| Messaging | Apache Kafka (Strimzi, KRaft) |
| Aplikacje | .NET 8, Confluent.Kafka, Spectre.Console |
| Narzędzia | Helm, kubectl |

## Struktura projektu

```
├── ansible/
│   ├── ansible.cfg
│   ├── inventory/hosts.yml
│   └── playbooks/
│       ├── site.yml              # Master playbook
│       ├── 01-minikube.yml       # Start klastra Minikube
│       ├── 02-vault.yml          # Deploy Vault w Docker
│       ├── 03-argocd.yml         # Instalacja ArgoCD
│       ├── 04-eso.yml            # Instalacja ESO (Helm)
│       ├── 05-configure.yml      # Konfiguracja Vault auth + ESO
│       └── 06-kafka.yml          # Instalacja Kafka (Strimzi)
├── k8s/
│   ├── namespace.yml             # Namespace demo-app
│   ├── service-account.yml       # ServiceAccount dla ESO
│   ├── secret-store.yml          # SecretStore (połączenie z Vault)
│   ├── external-secret.yml       # ExternalSecret (sync sekretów)
│   ├── nginx/
│   │   ├── deployment.yml        # Nginx z sekretami jako env vars
│   │   └── service.yml           # Service (NodePort)
│   └── kafka/
│       ├── strimzi-namespace.yml
│       ├── kafka-cluster.yml     # Klaster Kafka (KRaft, 1 broker)
│       └── kafka-topic.yml       # Topic: demo-messages
├── argocd/
│   └── nginx-secret-app.yml     # ArgoCD Application
├── apps/
│   ├── KafkaProducer/           # .NET 8 producer
│   └── KafkaConsumer/           # .NET 8 consumer
└── scripts/
    ├── build-apps.sh            # Budowanie aplikacji .NET
    └── cleanup.sh               # Czyszczenie zasobów
```

## Wymagania

- Docker
- Minikube
- kubectl
- Helm
- Ansible
- .NET 8 SDK (opcjonalnie, dla aplikacji Kafka)

## Szybki start

### 1. Deploy całego stacka (jeden command)

```bash
cd ansible
ansible-playbook playbooks/site.yml
```

Master playbook wykonuje kolejno:

1. Start Minikube (Docker driver, 4GB RAM, 2 CPU)
2. Deploy Vault w kontenerze Docker (dev mode, port 8200)
3. Instalacja ArgoCD
4. Instalacja External Secrets Operator
5. Konfiguracja Vault K8s auth + zasoby ESO
6. Instalacja Kafka (Strimzi operator)
7. Deploy aplikacji ArgoCD (Nginx z sekretami)

### 2. Deploy pojedynczych komponentów

```bash
ansible-playbook playbooks/01-minikube.yml
ansible-playbook playbooks/02-vault.yml
# itd.
```

### 3. Budowanie aplikacji Kafka

```bash
./scripts/build-apps.sh
```

### 4. Czyszczenie

```bash
./scripts/cleanup.sh
```

## Przepływ sekretów

```
Vault (secret/data/myapp/config)
  ├── username: admin
  └── password: s3cr3tP@ss
         │
         │  K8s Auth (ServiceAccount: eso-vault-auth)
         ▼
SecretStore (vault-secret-store)
         │
         │  ExternalSecret (odświeżanie co 15s)
         ▼
K8s Secret (myapp-secret)
         │
         │  env vars
         ▼
Nginx Pod
  ├── SECRET_USERNAME
  └── SECRET_PASSWORD
```

## Dostęp do komponentów

| Komponent | Dostęp |
|-----------|--------|
| Vault | `http://127.0.0.1:8200` (token: `root`) |
| ArgoCD | `kubectl port-forward svc/argocd-server -n argocd 8080:443` |
| Nginx | `minikube service nginx-secret-service -n demo-app` |
| Kafka | `localhost:31094` (NodePort) |
