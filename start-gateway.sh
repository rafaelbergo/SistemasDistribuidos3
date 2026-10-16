#!/bin/bash

# ============================================================
# SistemasDistribuidos3 - Inicialização no modo Frontend/Gateway
# ------------------------------------------------------------
# Igual ao start-services.sh, porém com os ajustes que o fluxo
# Frontend -> API Gateway exige:
#   - MS.Estoque sobe em HTTPS (porta 7072), que o MS.Principal consome
#   - MS.Principal roda com SSL_CERT_DIR para confiar no dev-cert do
#     MS.Estoque (senão a chamada gateway->estoque falha com erro de TLS)
# O start-services.sh original permanece inalterado.
# ============================================================

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

SERVICES=(
    "MS.Estoque"
    "MS.Pagamento"
    "MS.Promocoes"
    "Consumidor.C1"
    "Consumidor.C2"
    "MS.Entrega"
    "MS.Principal"
)

# Comando dotnet usado por serviço (os demais usam "dotnet run" puro)
run_cmd_for() {
    case "$1" in
        "MS.Estoque")
            echo 'dotnet run --launch-profile https'
            ;;
        "MS.Principal")
            echo 'export SSL_CERT_DIR="$HOME/.aspnet/dev-certs/trust:/etc/ssl/certs"; dotnet run'
            ;;
        *)
            echo 'dotnet run'
            ;;
    esac
}

# Função para encerrar processos e janelas de terminais abertos anteriormente
encerrar_instancias_anteriores() {
    echo "=========================================="
    echo " Verificando instâncias anteriores..."
    echo "=========================================="

    local PIDS=""
    for SERVICE in "${SERVICES[@]}"; do
        local SERVICE_PIDS
        SERVICE_PIDS=$(pgrep -f "$PROJECT_DIR/$SERVICE" 2>/dev/null | grep -v -E "^($$|$PPID)$" || true)
        if [ -n "$SERVICE_PIDS" ]; then
            PIDS="$PIDS $SERVICE_PIDS"
        fi

        local BIN_PIDS
        BIN_PIDS=$(pgrep -x "$SERVICE" 2>/dev/null | grep -v -E "^($$|$PPID)$" || true)
        if [ -n "$BIN_PIDS" ]; then
            PIDS="$PIDS $BIN_PIDS"
        fi
    done

    # Remove duplicatas e formata lista
    PIDS=$(echo "$PIDS" | tr ' ' '\n' | sort -u | tr '\n' ' ' | xargs)

    if [ -n "$PIDS" ]; then
        echo "Fechando processos e terminais abertos: $PIDS"
        kill -TERM $PIDS 2>/dev/null || true
        sleep 1

        # Verifica se algum processo ainda permaneceu ativo e força encerramento
        local REMAINING=""
        for PID in $PIDS; do
            if kill -0 "$PID" 2>/dev/null; then
                REMAINING="$REMAINING $PID"
            fi
        done

        if [ -n "$REMAINING" ]; then
            kill -9 $REMAINING 2>/dev/null || true
        fi
        echo "Instâncias anteriores finalizadas."
    else
        echo "Nenhuma instância anterior em execução."
    fi
    echo
}

# Se chamado com argumento de parada (--stop, --kill, etc.) apenas encerra
if [ "$1" = "--stop" ] || [ "$1" = "-s" ] || [ "$1" = "--kill" ] || [ "$1" = "-k" ]; then
    encerrar_instancias_anteriores
    exit 0
fi

# Fecha instâncias antigas antes de iniciar as novas
encerrar_instancias_anteriores

# Verifica se o diretório existe
if [ ! -d "$PROJECT_DIR" ]; then
    echo "ERRO: Diretório do projeto não encontrado:"
    echo "$PROJECT_DIR"
    exit 1
fi

# Verifica se o .NET está instalado
if ! command -v dotnet &> /dev/null; then
    echo "ERRO: .NET SDK não encontrado."
    exit 1
fi

# Verifica se a solution existe
if [ ! -f "$PROJECT_DIR/SistemasDistribuidos2.slnx" ]; then
    echo "ERRO: SistemasDistribuidos2.slnx não encontrado."
    exit 1
fi

# Garante que o dev-cert HTTPS exista e esteja confiável (melhor esforço)
echo "Verificando certificado de desenvolvimento HTTPS..."
dotnet dev-certs https --trust > /dev/null 2>&1 || true
echo

# Verifica se o RabbitMQ está ouvindo na porta 5672
if ! timeout 1 bash -c 'cat < /dev/null > /dev/tcp/localhost/5672' 2>/dev/null; then
    echo "=========================================="
    echo "AVISO: RabbitMQ não detectado na porta 5672."
    echo "Suba o container antes de continuar:"
    echo "docker compose up -d rabbitmq"
    echo "=========================================="
    echo
fi

# Detecta emulador de terminal disponível
TERMINAL_CMD=""
if command -v gnome-terminal &> /dev/null; then
    TERMINAL_CMD="gnome-terminal"
elif command -v x-terminal-emulator &> /dev/null; then
    TERMINAL_CMD="x-terminal-emulator"
else
    echo "ERRO: Nenhum emulador de terminal gráfico encontrado (gnome-terminal ou x-terminal-emulator)."
    echo "Instale com:"
    echo "sudo apt install gnome-terminal"
    exit 1
fi

echo "=========================================="
echo " Sistemas Distribuídos 3 - modo Gateway"
echo " Iniciando microserviços..."
echo "=========================================="
echo

cd "$PROJECT_DIR" || exit 1

# Corpo executado dentro de cada terminal. Recebe: $1=PROJECT $2=SERVICE $3=RUN_CMD
TERMINAL_BODY='
    PROJECT="$1"
    SERVICE="$2"
    RUN_CMD="$3"
    cd "$PROJECT" || exit 1

    echo "=========================================="
    echo " $SERVICE"
    echo "=========================================="
    echo
    echo "Diretório: $(pwd)"
    echo "Comando:   $RUN_CMD"
    echo "Iniciando aplicação..."
    echo

    eval "$RUN_CMD"
    EXIT_CODE=$?

    echo
    echo "=========================================="
    echo " $SERVICE finalizado (Código de saída: $EXIT_CODE)"
    echo "=========================================="
    echo
    read -p "Pressione ENTER para fechar..."
'

for SERVICE in "${SERVICES[@]}"; do

    PROJECT="$PROJECT_DIR/$SERVICE"

    if [ ! -d "$PROJECT" ]; then
        echo "AVISO: Projeto não encontrado: $SERVICE"
        continue
    fi

    RUN_CMD="$(run_cmd_for "$SERVICE")"
    echo "Iniciando: $SERVICE  ($RUN_CMD)"

    if [ "$TERMINAL_CMD" = "gnome-terminal" ]; then
        gnome-terminal --title="$SERVICE" -- bash -c "$TERMINAL_BODY" _ "$PROJECT" "$SERVICE" "$RUN_CMD"
    else
        x-terminal-emulator -T "$SERVICE" -e bash -c "$TERMINAL_BODY" _ "$PROJECT" "$SERVICE" "$RUN_CMD"
    fi

    sleep 1
done

echo
echo "=========================================="
echo " Todos os microserviços foram iniciados."
echo " Catálogo via gateway: http://localhost:5059/api/catalogo"
echo " Frontend (em front-end/): npm run dev  ->  http://localhost:5173"
echo "=========================================="
