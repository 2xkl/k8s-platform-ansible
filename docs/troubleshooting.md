# Troubleshooting

Przewodnik debugowania komponentow platformy. Opisane problemy zostaly napotkane podczas wdrazania i uzywania tego projektu.

---

## ArgoCD

### Logi i status

```bash
# Status wszystkich aplikacji
kubectl get applications -n argocd

# Szczegoly konkretnej aplikacji (eventy, warunki, bledy sync)
kubectl describe application <nazwa> -n argocd

# Pelny YAML z sekcja status (bledy, operationState, conditions)
kubectl get application <nazwa> -n argocd -o yaml

# Logi application-controllera (odpowiada za sync)
kubectl logs -n argocd -l app.kubernetes.io/name=argocd-application-controller --tail=100

# Logi repo-servera (klonowanie repo, renderowanie manifestow)
kubectl logs -n argocd -l app.kubernetes.io/name=argocd-repo-server --tail=100

# Logi argocd-servera (API/UI)
kubectl logs -n argocd -l app.kubernetes.io/name=argocd-server --tail=100
```

### Problem: Sync status Unknown -- "Object 'Kind' is missing"

**Objaw:** Aplikacja pokazuje `Sync: Unknown`, w `conditions` pojawia sie `ComparisonError` z komunikatem:

```
Failed to load target state: failed to unmarshal manifests -- Object 'Kind' is missing
```

**Przyczyna:** ArgoCD skanuje katalog wskazany w `spec.source.path` i probuje sparsowac kazdy plik YAML/JSON jako manifest Kubernetes. Jesli w tym katalogu znajduja sie pliki nie bedace manifestami K8s (np. konfiguracja Keycloak realm, pliki konfiguracyjne aplikacji), ArgoCD nie moze ich sparsowac.

**Diagnostyka:**

```bash
# Sprawdz jaki path skanuje aplikacja
kubectl get application <nazwa> -n argocd -o jsonpath='{.spec.source.path}'

# Sprawdz blad
kubectl get application <nazwa> -n argocd -o jsonpath='{.status.conditions[*].message}'
```

**Rozwiazanie:**
- Przenies pliki nie bedace manifestami K8s poza skanowany katalog (np. do `config/`)
- Lub zawez `spec.source.path` do konkretnego podkatalogu
- Lub uzyj `spec.source.directory.exclude` aby wykluczyc konkretne pliki

### Problem: Aplikacja zablokowana na usuwaniu (stuck finalizer)

**Objaw:** Aplikacja ma ustawiony `deletionTimestamp` ale nie znika. `kubectl delete` wisi w nieskonczonosc.

**Diagnostyka:**

```bash
# Sprawdz czy jest finalizer
kubectl get application <nazwa> -n argocd -o jsonpath='{.metadata.finalizers}'
```

**Rozwiazanie:**

```bash
# Usun finalizer zeby odblokowac kasowanie
kubectl patch application <nazwa> -n argocd --type merge \
  -p '{"metadata":{"finalizers":null}}'
```

### Problem: OutOfSync po zmianie nazwy repo

**Objaw:** Po zmianie nazwy repozytorium na GitHubie aplikacja traci sync, bo `repoURL` wskazuje na stary adres.

**Rozwiazanie:**

```bash
# Zaktualizuj repoURL w aplikacji
kubectl patch application <nazwa> -n argocd --type merge \
  -p '{"spec":{"source":{"repoURL":"https://github.com/<user>/<nowa-nazwa>.git"}}}'

# Zaktualizuj tez manifest YAML w repo zeby bylo spojne
```

### Problem: Sync Failed -- blad w manifescie

**Diagnostyka:**

```bash
# Sprawdz operationState -- zawiera dokladny blad ostatniego sync
kubectl get application <nazwa> -n argocd \
  -o jsonpath='{.status.operationState.message}'

# Lista zasobow i ich status
kubectl get application <nazwa> -n argocd \
  -o jsonpath='{range .status.resources[*]}{.kind}/{.name}: {.status}{"\n"}{end}'

# Wymus ponowny sync
kubectl patch application <nazwa> -n argocd --type merge \
  -p '{"metadata":{"annotations":{"argocd.argoproj.io/refresh":"hard"}}}'
```

---

## External Secrets Operator (ESO)

### Logi i status

```bash
# Status SecretStore
kubectl get secretstore -n demo-app
kubectl describe secretstore vault-secret-store -n demo-app

# Status ExternalSecret (pokazuje czy sync dziala)
kubectl get externalsecret -n demo-app
kubectl describe externalsecret -n demo-app

# Logi operatora ESO
kubectl logs -n external-secrets -l app.kubernetes.io/name=external-secrets --tail=100

# Sprawdz czy K8s Secret zostal utworzony
kubectl get secret myapp-secret -n demo-app -o yaml
```

### Problem: SecretStore -- status not ready / connection refused

**Objaw:** `SecretStore` ma status `Valid: False`, ExternalSecret nie synchronizuje sekretow.

**Diagnostyka:**

```bash
# Sprawdz status SecretStore
kubectl get secretstore -n demo-app -o yaml

# Sprawdz eventy
kubectl get events -n demo-app --field-selector involvedObject.kind=SecretStore
```

**Czeste przyczyny:**
- Vault nie jest dostepny pod adresem skonfigurowanym w SecretStore
- ServiceAccount nie ma uprawnien do autentykacji w Vault
- Vault K8s auth backend nie jest poprawnie skonfigurowany

**Debugowanie polaczenia Vault <-> K8s:**

```bash
# Sprawdz czy ServiceAccount istnieje
kubectl get sa eso-vault-auth -n demo-app

# Sprawdz czy Vault jest osiagalny z klastra
kubectl run vault-test --rm -it --image=curlimages/curl --restart=Never -- \
  curl -s http://<vault-address>:8200/v1/sys/health

# Sprawdz Vault K8s auth config
vault read auth/kubernetes/config
```

### Problem: ExternalSecret -- SecretSyncedError

**Objaw:** ExternalSecret ma status `SecretSyncedError`, K8s Secret nie jest tworzony lub nie aktualizuje sie.

**Diagnostyka:**

```bash
# Szczegoly bledu
kubectl describe externalsecret <nazwa> -n demo-app

# Sprawdz czy sciezka w Vault jest poprawna
vault kv get secret/myapp/config

# Sprawdz czy klucze w ExternalSecret pasuja do kluczy w Vault
kubectl get externalsecret <nazwa> -n demo-app -o yaml
```

**Czeste przyczyny:**
- Bledna sciezka do sekretu w Vault (`remoteRef.key`)
- Bledna nazwa klucza (`remoteRef.property`)
- Token/auth wygasl -- restart poda ESO moze pomoc

---

## HashiCorp Vault

### Logi i status

```bash
# Status Vault (Docker)
docker ps | grep vault
docker logs vault

# Health check
curl -s http://127.0.0.1:8200/v1/sys/health | python3 -m json.tool

# Status seal
vault status

# Lista zamontowanych auth methods
vault auth list

# Lista secret engines
vault secrets list
```

### Problem: Vault sealed / not initialized

**Objaw:** Vault zwraca `503 Service Unavailable`, `vault status` pokazuje `Sealed: true`.

**Diagnostyka:**

```bash
vault status
curl -s http://127.0.0.1:8200/v1/sys/health
```

**Rozwiazanie:** W trybie dev Vault nie powinien byc sealed. Jesli kontener zostal zrestartowany:

```bash
# Restart kontenera Vault w trybie dev
docker restart vault

# Lub uruchom ponownie playbook
ansible-playbook ansible/playbooks/02-vault.yml
```

### Problem: K8s auth -- permission denied

**Objaw:** ESO nie moze sie zalogowac do Vault, w logach `permission denied`.

**Diagnostyka:**

```bash
# Sprawdz konfiguracje K8s auth w Vault
vault read auth/kubernetes/config

# Sprawdz role
vault read auth/kubernetes/role/eso-role

# Sprawdz policy
vault policy read eso-policy

# Test logowania z poziomu poda
kubectl run vault-login-test --rm -it --image=curlimages/curl --restart=Never -- \
  sh -c 'TOKEN=$(cat /var/run/secrets/kubernetes.io/serviceaccount/token) && \
  curl -s --request POST \
  --data "{\"jwt\": \"$TOKEN\", \"role\": \"eso-role\"}" \
  http://<vault-address>:8200/v1/auth/kubernetes/login'
```

**Czeste przyczyny:**
- `kubernetes_host` w Vault auth config nie zgadza sie z adresem API servera
- ServiceAccount lub namespace nie pasuja do roli w Vault
- CA cert nie jest poprawny

### Problem: Sekret nie istnieje w Vault

```bash
# Listuj sekrety
vault kv list secret/

# Sprawdz konkretny sekret
vault kv get secret/myapp/config

# Dodaj sekret jesli brakuje
vault kv put secret/myapp/config username=admin password=s3cr3tP@ss
```

---

## Apache Kafka (Strimzi)

### Logi i status

```bash
# Status podow Kafka
kubectl get pods -n kafka

# Status klastra Kafka (custom resource)
kubectl get kafka -n kafka
kubectl describe kafka demo-kafka -n kafka

# Logi brokera
kubectl logs demo-kafka-dual-role-0 -n kafka --tail=100

# Logi Strimzi operatora
kubectl logs -n kafka -l strimzi.io/kind=cluster-operator --tail=100

# Lista topikow (z poda brokera)
kubectl exec -n kafka demo-kafka-dual-role-0 -- \
  bin/kafka-topics.sh --bootstrap-server localhost:9092 --list

# Szczegoly topiku
kubectl exec -n kafka demo-kafka-dual-role-0 -- \
  bin/kafka-topics.sh --bootstrap-server localhost:9092 \
  --describe --topic demo-messages

# Sprawdz KafkaTopic CR
kubectl get kafkatopic -n kafka
kubectl describe kafkatopic demo-messages -n kafka
```

### Problem: Broker nie startuje -- CrashLoopBackOff

**Diagnostyka:**

```bash
# Logi poda
kubectl logs demo-kafka-dual-role-0 -n kafka --previous

# Eventy
kubectl get events -n kafka --sort-by='.lastTimestamp'

# Sprawdz PVC (moze brak miejsca)
kubectl get pvc -n kafka
```

**Czeste przyczyny:**
- Brak zasobow (RAM/CPU) w Minikube -- sprawdz `minikube config view`
- PersistentVolume pelny lub niedostepny
- Bledna konfiguracja KRaft (controller quorum)

### Problem: Nie mozna polaczyc sie z Kafka z hosta (localhost:31094)

**Diagnostyka:**

```bash
# Sprawdz czy NodePort serwis istnieje
kubectl get svc -n kafka | grep nodeport

# Sprawdz IP minikube
minikube ip

# Sprawdz czy port jest otwarty
nc -zv $(minikube ip) 31094

# Sprawdz listenery w konfiguracji klastra
kubectl get kafka demo-kafka -n kafka -o jsonpath='{.spec.kafka.listeners}' | python3 -m json.tool
```

**Rozwiazanie:** Jesli NodePort nie odpowiada:

```bash
# Sprawdz czy minikube tunnel jest potrzebny
minikube service list -n kafka
```

### Problem: Consumer nie odbiera wiadomosci

**Diagnostyka:**

```bash
# Sprawdz consumer groups
kubectl exec -n kafka demo-kafka-dual-role-0 -- \
  bin/kafka-consumer-groups.sh --bootstrap-server localhost:9092 \
  --describe --group demo-consumer-group

# Sprawdz czy topic ma wiadomosci
kubectl exec -n kafka demo-kafka-dual-role-0 -- \
  bin/kafka-console-consumer.sh --bootstrap-server localhost:9092 \
  --topic demo-messages --from-beginning --max-messages 5
```

**Czeste przyczyny:**
- Consumer group ma juz przeczytane wszystkie wiadomosci (offset na koncu)
- Bledny bootstrap server address
- Topic nie istnieje

### Problem: Entity operator restart / topic nie tworzy sie

**Diagnostyka:**

```bash
# Logi entity operatora
kubectl logs -n kafka -l strimzi.io/name=demo-kafka-entity-operator --tail=100

# Sprawdz KafkaTopic status
kubectl get kafkatopic demo-messages -n kafka -o yaml
```

---

## Keycloak

### Logi i status

```bash
# Status poda
kubectl get pods -n keycloak

# Logi Keycloak
kubectl logs -n keycloak -l app=keycloak --tail=100

# Health check
curl -s http://$(minikube ip):30080/health

# Sprawdz czy realm istnieje
curl -s http://$(minikube ip):30080/realms/demo
```

### Problem: Keycloak nie startuje -- CrashLoopBackOff

**Diagnostyka:**

```bash
kubectl logs -n keycloak -l app=keycloak --previous
kubectl describe pod -n keycloak -l app=keycloak
kubectl get events -n keycloak --sort-by='.lastTimestamp'
```

**Czeste przyczyny:**
- Brak zasobow (Keycloak potrzebuje min. ~512MB RAM)
- Port 8080 juz zajety w kontenerze

### Problem: Nie mozna uzyskac tokenu -- 401/403

**Diagnostyka:**

```bash
# Test pobrania tokenu
curl -s -X POST \
  http://$(minikube ip):30080/realms/demo/protocol/openid-connect/token \
  -d "grant_type=password" \
  -d "client_id=demo-api" \
  -d "client_secret=demo-api-secret" \
  -d "username=testuser" \
  -d "password=testpass"

# Sprawdz konfiguracje realm
curl -s http://$(minikube ip):30080/realms/demo/.well-known/openid-configuration | python3 -m json.tool
```

**Czeste przyczyny:**
- Realm `demo` nie zostal utworzony (playbook 07-keycloak nie dokonczyl sie)
- Client `demo-api` nie istnieje lub ma inny secret
- User `testuser` nie istnieje lub ma inne haslo

**Ponowne utworzenie realm:**

```bash
ansible-playbook ansible/playbooks/07-keycloak.yml
```

### Problem: DemoApi nie moze zweryfikowac tokenu JWT

**Objaw:** DemoApi zwraca 401 mimo poprawnego tokenu.

**Diagnostyka:**

```bash
# Logi DemoApi
kubectl logs -n demo-app -l app=demo-api --tail=100

# Sprawdz zmienna srodowiskowa Authority
kubectl get deployment demo-api -n demo-app \
  -o jsonpath='{.spec.template.spec.containers[0].env}' | python3 -m json.tool

# Sprawdz czy Keycloak jest osiagalny z klastra
kubectl run kc-test --rm -it --image=curlimages/curl --restart=Never -- \
  curl -s http://keycloak.keycloak.svc.cluster.local:8080/realms/demo
```

**Czeste przyczyny:**
- `Keycloak__Authority` wskazuje na zly adres (musi byc adres wewnetrzny klastra)
- Keycloak nie jest dostepny z namespace DemoApi (sprawdz NetworkPolicy)

---

## Ogolne komendy diagnostyczne

```bash
# Status calego klastra
minikube status
kubectl get nodes
kubectl cluster-info

# Pody we wszystkich namespace'ach
kubectl get pods -A

# Eventy z calego klastra (posortowane chronologicznie)
kubectl get events -A --sort-by='.lastTimestamp' | tail -30

# Zuzycie zasobow
kubectl top nodes
kubectl top pods -A

# Restart poda (usuwajac go -- deployment utworzy nowy)
kubectl delete pod <nazwa-poda> -n <namespace>

# Sprawdz logi poda ktory sie restartuje
kubectl logs <nazwa-poda> -n <namespace> --previous
```
