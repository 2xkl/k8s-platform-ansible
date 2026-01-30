#!/bin/bash
set -e

echo "=== Cleanup: K8s ESO Vault ArgoCD Kafka Keycloak ==="

echo "[1/6] Deleting ArgoCD Application..."
kubectl delete application nginx-secret-app -n argocd --ignore-not-found=true

echo "[2/6] Deleting demo-app namespace..."
kubectl delete namespace demo-app --ignore-not-found=true

echo "[3/6] Deleting Kafka cluster and namespace..."
kubectl delete kafka demo-kafka -n kafka --ignore-not-found=true 2>/dev/null || true
kubectl delete namespace kafka --ignore-not-found=true

echo "[4/6] Deleting Keycloak namespace..."
kubectl delete namespace keycloak --ignore-not-found=true

echo "[5/6] Stopping Vault container..."
docker stop vault-dev 2>/dev/null || true
docker rm vault-dev 2>/dev/null || true

echo "[6/6] Stopping Minikube..."
minikube stop

echo ""
echo "Cleanup complete!"
echo "To fully delete minikube cluster: minikube delete"
