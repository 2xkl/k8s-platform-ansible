#!/bin/bash
set -e

echo "=== Cleanup: K8s ESO Vault ArgoCD ==="

echo "[1/4] Deleting ArgoCD Application..."
kubectl delete application nginx-secret-app -n argocd --ignore-not-found=true

echo "[2/4] Deleting demo-app namespace..."
kubectl delete namespace demo-app --ignore-not-found=true

echo "[3/4] Stopping Vault container..."
docker stop vault-dev 2>/dev/null || true
docker rm vault-dev 2>/dev/null || true

echo "[4/4] Stopping Minikube..."
minikube stop

echo ""
echo "Cleanup complete!"
echo "To fully delete minikube cluster: minikube delete"
