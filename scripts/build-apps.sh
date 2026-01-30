#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
APPS_DIR="$PROJECT_DIR/apps"
OUTPUT_DIR="$APPS_DIR/bin"

echo "=== Budowanie aplikacji Kafka Producer/Consumer ==="

# Sprawdzenie .NET SDK
if ! command -v dotnet &> /dev/null; then
    echo "BLAD: .NET SDK nie jest zainstalowany."
    echo ""
    echo "Instalacja na Ubuntu/Debian:"
    echo "  sudo apt-get update && sudo apt-get install -y dotnet-sdk-8.0"
    echo ""
    echo "Lub przez snap:"
    echo "  sudo snap install dotnet-sdk --classic --channel=8.0"
    echo ""
    echo "Wiecej: https://learn.microsoft.com/en-us/dotnet/core/install/linux"
    exit 1
fi

echo "Wersja .NET SDK: $(dotnet --version)"
echo ""

mkdir -p "$OUTPUT_DIR"

echo "[1/2] Budowanie KafkaProducer..."
dotnet publish "$APPS_DIR/KafkaProducer/KafkaProducer.csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=true \
    -o "$OUTPUT_DIR"

echo ""
echo "[2/2] Budowanie KafkaConsumer..."
dotnet publish "$APPS_DIR/KafkaConsumer/KafkaConsumer.csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=true \
    -o "$OUTPUT_DIR"

echo ""
echo "=== Gotowe! ==="
echo "Binarki w: $OUTPUT_DIR"
ls -lh "$OUTPUT_DIR"/Kafka*
echo ""
echo "Uzycie:"
echo "  $OUTPUT_DIR/KafkaProducer [bootstrap-server]"
echo "  $OUTPUT_DIR/KafkaConsumer [bootstrap-server]"
echo ""
echo "Domyslny bootstrap server: localhost:31094"
echo "Uzyj 'minikube ip' aby pobrac IP node'a."
