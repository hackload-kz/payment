#!/bin/bash

# SPDX-License-Identifier: MIT
# Copyright (c) 2025 HackLoad Payment Gateway

# HackLoad Payment Gateway - Docker Build and Run Script
# This script builds and runs the payment gateway using Docker

set -e  # Exit on any error

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR"

# Environment Configuration
ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"
BASE_URL="${BASE_URL:-http://localhost:7010}"

# Database Configuration
DB_HOST="${DB_HOST:-host.docker.internal}"
DB_PORT="${DB_PORT:-5432}"
DB_NAME="${DB_NAME:-task}"
DB_USER="${DB_USER:-organizer}"
DB_PASSWORD="${DB_PASSWORD:-password}"

# Security Configuration
CSRF_KEY="${CSRF_KEY:-hackload-payment-gateway-csrf-key-2025}"
ADMIN_TOKEN="${ADMIN_TOKEN:-admin_token_2025_hackload_payment_gateway_secure_key_dev_only}"
ADMIN_KEY="${ADMIN_KEY:-$ADMIN_TOKEN}"

# Docker Configuration
DOCKER_IMAGE_NAME="${DOCKER_IMAGE_NAME:-hackload-paymentgateway}"
DOCKER_IMAGE_TAG="${DOCKER_IMAGE_TAG:-latest}"
DOCKER_CONTAINER_NAME="${DOCKER_CONTAINER_NAME:-payment-gateway}"
DOCKER_HTTP_PORT="${DOCKER_HTTP_PORT:-7010}"
DOCKER_METRICS_PORT="${DOCKER_METRICS_PORT:-8081}"
DOCKER_NETWORK="${DOCKER_NETWORK:-hackload-network}"
USE_HOST_NETWORK="${USE_HOST_NETWORK:-false}"

# Function to print colored output
print_message() {
    local color=$1
    local message=$2
    echo -e "${color}${message}${NC}"
}

print_header() {
    print_message $BLUE "=================================================="
    print_message $BLUE "$1"
    print_message $BLUE "=================================================="
}

print_success() {
    print_message $GREEN "✅ $1"
}

print_warning() {
    print_message $YELLOW "⚠️  $1"
}

print_error() {
    print_message $RED "❌ $1"
}

# Function to create Docker network
create_docker_network() {
    if [ "$USE_HOST_NETWORK" = "false" ]; then
        print_header "Setting up Docker Network"
        
        # Check if network already exists
        if docker network ls --format '{{.Name}}' | grep -q "^${DOCKER_NETWORK}$"; then
            print_success "Docker network already exists: $DOCKER_NETWORK"
        else
            print_message $BLUE "Creating Docker network: $DOCKER_NETWORK"
            docker network create "$DOCKER_NETWORK"
            
            if [ $? -eq 0 ]; then
                print_success "Docker network created: $DOCKER_NETWORK"
            else
                print_error "Failed to create Docker network"
                exit 1
            fi
        fi
        
        # Show network information
        print_message $BLUE "Network details:"
        docker network inspect "$DOCKER_NETWORK" --format '{{.Name}}: {{.Driver}} ({{range .IPAM.Config}}{{.Subnet}}{{end}})'
    else
        print_message $BLUE "Using host network mode"
    fi
}

# Function to check prerequisites
check_prerequisites() {
    print_header "Checking Prerequisites"
    
    # Check if Docker is installed
    if ! command -v docker &> /dev/null; then
        print_error "Docker is not installed. Please install Docker."
        exit 1
    fi
    
    local docker_version=$(docker --version)
    print_success "Docker version: $docker_version"
    
    # Check if Docker daemon is running
    if ! docker info &> /dev/null; then
        print_error "Docker daemon is not running. Please start Docker."
        exit 1
    fi
    
    print_success "Docker daemon is running"
    
    # Check if Dockerfile exists
    if [ ! -f "$PROJECT_DIR/Dockerfile" ]; then
        print_error "Dockerfile not found: $PROJECT_DIR/Dockerfile"
        exit 1
    fi
    
    print_success "Dockerfile found: $PROJECT_DIR/Dockerfile"
}

# Function to build Docker image using docker-build.sh
build_docker_image() {
    print_header "Building Docker Image"
    
    cd "$PROJECT_DIR"
    
    # Check if docker-build.sh exists
    if [ ! -f "$PROJECT_DIR/docker-build.sh" ]; then
        print_error "docker-build.sh not found: $PROJECT_DIR/docker-build.sh"
        print_message $YELLOW "Falling back to direct docker build..."
        
        local full_image_name="$DOCKER_IMAGE_NAME:$DOCKER_IMAGE_TAG"
        print_message $BLUE "Building Docker image: $full_image_name"
        
        docker build -t "$full_image_name" \
            --target runtime \
            --build-arg ASPNETCORE_ENVIRONMENT="$ASPNETCORE_ENVIRONMENT" \
            .
        
        if [ $? -eq 0 ]; then
            print_success "Docker image built successfully: $full_image_name"
        else
            print_error "Failed to build Docker image"
            exit 1
        fi
        return
    fi
    
    print_message $BLUE "Using docker-build.sh for building Docker image"
    print_message $BLUE "Image name: $DOCKER_IMAGE_NAME"
    print_message $BLUE "Image tag: $DOCKER_IMAGE_TAG"
    
    # Set environment variables for docker-build.sh
    export DOCKER_REGISTRY="${DOCKER_REGISTRY:-ghcr.io}"
    export DOCKER_NAMESPACE="${DOCKER_NAMESPACE:-hackload-kz}"
    export DOCKER_IMAGE_NAME="$DOCKER_IMAGE_NAME"
    export DOCKERFILE="${DOCKERFILE:-Dockerfile}"
    
    # Run docker-build.sh (build only, no push)
    print_message $BLUE "Running docker-build.sh..."
    bash "$PROJECT_DIR/docker-build.sh"
    
    if [ $? -eq 0 ]; then
        print_success "Docker image built successfully using docker-build.sh"
        
        # Show the built images
        print_message $BLUE "Available Docker images:"
        docker images "${DOCKER_REGISTRY}/${DOCKER_NAMESPACE}/${DOCKER_IMAGE_NAME}" --format "table {{.Repository}}\t{{.Tag}}\t{{.Size}}\t{{.CreatedAt}}"
        
        # Tag the image with our local tag if different
        local full_registry_name="${DOCKER_REGISTRY}/${DOCKER_NAMESPACE}/${DOCKER_IMAGE_NAME}:latest"
        local local_name="${DOCKER_IMAGE_NAME}:${DOCKER_IMAGE_TAG}"
        
        if [ "$full_registry_name" != "$local_name" ]; then
            print_message $BLUE "Creating local tag: $local_name"
            docker tag "$full_registry_name" "$local_name"
            
            if [ $? -eq 0 ]; then
                print_success "Local tag created: $local_name"
            else
                print_warning "Failed to create local tag, will use registry name"
                # Update the local variables to use the registry name
                DOCKER_IMAGE_NAME="${DOCKER_REGISTRY}/${DOCKER_NAMESPACE}/${DOCKER_IMAGE_NAME}"
                DOCKER_IMAGE_TAG="latest"
            fi
        fi
    else
        print_error "Failed to build Docker image using docker-build.sh"
        exit 1
    fi
}

# Function to stop existing container
stop_existing_container() {
    if docker ps -a --format '{{.Names}}' | grep -q "^${DOCKER_CONTAINER_NAME}$"; then
        print_message $BLUE "Stopping existing container: $DOCKER_CONTAINER_NAME"
        docker stop "$DOCKER_CONTAINER_NAME" &> /dev/null || true
        docker rm "$DOCKER_CONTAINER_NAME" &> /dev/null || true
        print_success "Existing container stopped and removed"
    fi
}

# Function to start Docker container
start_docker_container() {
    print_header "Starting Docker Container"
    
    stop_existing_container
    
    local full_image_name="$DOCKER_IMAGE_NAME:$DOCKER_IMAGE_TAG"
    
    print_message $BLUE "Starting container: $DOCKER_CONTAINER_NAME"
    print_message $BLUE "Image: $full_image_name"
    
    # Build Docker run command based on network mode
    local docker_args=""
    
    # Add network configuration
    if [ "$USE_HOST_NETWORK" = "true" ]; then
        docker_args+=" --network host"
        print_message $BLUE "Using host network mode (no port mapping needed)"
    else
        if docker network ls --format '{{.Name}}' | grep -q "^${DOCKER_NETWORK}$"; then
            docker_args+=" --network $DOCKER_NETWORK"
            print_message $BLUE "Using custom network: $DOCKER_NETWORK"
        fi
        docker_args+=" -p $DOCKER_HTTP_PORT:8080"
        docker_args+=" -p $DOCKER_METRICS_PORT:8081"
        print_message $BLUE "Port mappings: $DOCKER_HTTP_PORT:8080, $DOCKER_METRICS_PORT:8081"
    fi
    
    # Run the container
    docker run -d \
        --name "$DOCKER_CONTAINER_NAME" \
        $docker_args \
        -e ASPNETCORE_ENVIRONMENT="$ASPNETCORE_ENVIRONMENT" \
        -e ASPNETCORE_URLS="http://0.0.0.0:8080" \
        -e ConnectionStrings__DefaultConnection="Host=$DB_HOST;Port=$DB_PORT;Database=$DB_NAME;Username=$DB_USER;Password=$DB_PASSWORD;Include Error Detail=true" \
        -e Security__CsrfKey="$CSRF_KEY" \
        -e AdminAuthentication__AdminToken="$ADMIN_TOKEN" \
        -e AdminAuthentication__TokenHeaderName="X-Admin-Token" \
        -e AdminAuthentication__EnableAdminEndpoints="true" \
        -e Api__BaseUrl="$BASE_URL" \
        -e Api__Version="v1" \
        -e Metrics__Prometheus__Enabled="true" \
        -e Metrics__Prometheus__Port="8081" \
        -e Metrics__Prometheus__Host="*" \
        -e Metrics__Dashboard__Enabled="true" \
        -e Database__EnableRetryOnFailure="true" \
        -e Database__MaxRetryCount="3" \
        -e Database__MaxRetryDelay="00:00:30" \
        -e Database__CommandTimeout="60" \
        -e Database__PoolSize="20" \
        --restart unless-stopped \
        --health-cmd="curl -f http://localhost:8080/health || exit 1" \
        --health-interval=30s \
        --health-timeout=3s \
        --health-retries=3 \
        "$full_image_name"
    
    if [ $? -eq 0 ]; then
        print_success "Container started successfully"
        print_message $BLUE "Container name: $DOCKER_CONTAINER_NAME"
        print_message $BLUE "API available at: http://localhost:$DOCKER_HTTP_PORT"
        print_message $BLUE "Metrics available at: http://localhost:$DOCKER_METRICS_PORT/metrics"
        print_message $BLUE ""
        print_message $BLUE "Use the following commands to manage the container:"
        print_message $BLUE "  docker logs $DOCKER_CONTAINER_NAME -f    # View logs"
        print_message $BLUE "  docker stop $DOCKER_CONTAINER_NAME       # Stop container"
        print_message $BLUE "  docker restart $DOCKER_CONTAINER_NAME    # Restart container"
        print_message $BLUE ""
        
        # Wait a moment and check if container is running
        print_message $BLUE "Waiting for container to start..."
        sleep 5
        
        if docker ps --format '{{.Names}}' | grep -q "^${DOCKER_CONTAINER_NAME}$"; then
            print_success "Container is running"
            
            # Check container health and logs
            print_message $BLUE "Container status:"
            docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}" --filter name="$DOCKER_CONTAINER_NAME"
            
            # Show recent logs
            print_message $BLUE "Recent container logs:"
            docker logs "$DOCKER_CONTAINER_NAME" --tail 20
            
            # Additional debugging
            print_message $BLUE "Container process info:"
            docker exec "$DOCKER_CONTAINER_NAME" ps aux 2>/dev/null || print_warning "Cannot access container processes"
            
            # Check what's listening inside the container
            print_message $BLUE "Ports listening inside container:"
            docker exec "$DOCKER_CONTAINER_NAME" netstat -tulpn 2>/dev/null || docker exec "$DOCKER_CONTAINER_NAME" ss -tulpn 2>/dev/null || print_warning "Cannot check container ports"
            
            # Test connectivity
            print_message $BLUE "Testing container connectivity..."
            local test_port
            local test_url
            
            if [ "$USE_HOST_NETWORK" = "true" ]; then
                test_port=8080
                test_url="http://localhost:8080"
            else
                test_port=$DOCKER_HTTP_PORT
                test_url="http://localhost:$DOCKER_HTTP_PORT"
            fi
            
            # Test port binding
            if ss -tulpn 2>/dev/null | grep -q ":${test_port} " || netstat -tulpn 2>/dev/null | grep -q ":${test_port} "; then
                print_success "Port $test_port is listening"
            else
                print_warning "Port $test_port may not be listening"
            fi
            
            # Try to curl the health endpoint if available
            sleep 2
            if curl -s -f "${test_url}/health" >/dev/null 2>&1; then
                print_success "Health check endpoint is responding"
            elif curl -s -f "${test_url}" >/dev/null 2>&1; then
                print_success "Application is responding on port $test_port"
            else
                print_warning "Application may not be ready yet (this is normal for .NET apps)"
                print_message $YELLOW "Try accessing $test_url in a few moments"
                
                # Additional debugging for host network
                if [ "$USE_HOST_NETWORK" = "true" ]; then
                    print_message $YELLOW "Host network troubleshooting:"
                    print_message $YELLOW "  Check if port 8080 is free: sudo lsof -i :8080"
                    print_message $YELLOW "  Check container process: docker exec $DOCKER_CONTAINER_NAME ps aux"
                    print_message $YELLOW "  Test from inside container: docker exec $DOCKER_CONTAINER_NAME curl -v http://localhost:8080"
                    print_message $YELLOW "  Check host ports: ss -tulpn | grep 8080"
                    
                    # Try to test from inside the container
                    print_message $BLUE "Testing from inside container..."
                    if docker exec "$DOCKER_CONTAINER_NAME" curl -s -f http://localhost:8080 >/dev/null 2>&1; then
                        print_success "Application responds from inside container"
                        print_error "Problem: Host network connectivity issue"
                        print_message $RED "The app works inside container but not accessible from host"
                        print_message $RED "This suggests a network configuration problem"
                    elif docker exec "$DOCKER_CONTAINER_NAME" curl -s -f http://127.0.0.1:8080 >/dev/null 2>&1; then
                        print_success "Application responds on 127.0.0.1:8080 inside container"
                        print_warning "App may only be binding to localhost, not 0.0.0.0"
                    else
                        print_warning "Application not responding inside container either"
                        print_message $YELLOW "The ASP.NET Core app may not be starting correctly"
                    fi
                fi
            fi
            
            print_message $BLUE ""
            print_success "Setup complete!"
            
            if [ "$USE_HOST_NETWORK" = "true" ]; then
                print_message $GREEN "🌐 Application: http://localhost:8080"
                print_message $GREEN "📊 Metrics: http://localhost:8081/metrics"
                print_message $YELLOW "Note: Host network mode uses container's internal ports (8080, 8081)"
            else
                print_message $GREEN "🌐 Application: http://localhost:$DOCKER_HTTP_PORT"
                print_message $GREEN "📊 Metrics: http://localhost:$DOCKER_METRICS_PORT/metrics"
            fi
        else
            print_error "Container failed to start properly"
            print_message $YELLOW "Container logs:"
            docker logs "$DOCKER_CONTAINER_NAME"
            exit 1
        fi
    else
        print_error "Failed to start container"
        exit 1
    fi
}

# Function to show usage
show_usage() {
    echo "Usage: $0 [OPTIONS]"
    echo ""
    echo "HackLoad Payment Gateway - Docker Build and Run Script"
    echo ""
    echo "Options:"
    echo "  --help          Show this help message"
    echo "  --build-only    Only build Docker image, don't run"
    echo "  --production    Run in production mode"
    echo "  --host-network  Use host network mode (for database containers on same host)"
    echo "  --stop          Stop running Docker container"
    echo "  --logs          Show Docker container logs"
    echo "  --debug         Show detailed debugging information"
    echo ""
    echo "Environment Variables:"
    echo "  ASPNETCORE_ENVIRONMENT    Environment (Development/Production) [default: Development]"
    echo "  BASE_URL                  Base URL for the API [default: http://localhost:7010]"
    echo "  DB_HOST                   Database host [default: localhost]"
    echo "  DB_PORT                   Database port [default: 5432]"
    echo "  DB_NAME                   Database name [default: task]"
    echo "  DB_USER                   Database user [default: organizer]"
    echo "  DB_PASSWORD               Database password [default: password]"
    echo "  ADMIN_KEY                 Admin authentication key [default: same as ADMIN_TOKEN]"
    echo "  DOCKER_IMAGE_NAME         Docker image name [default: hackload-paymentgateway]"
    echo "  DOCKER_IMAGE_TAG          Docker image tag [default: latest]"
    echo "  DOCKER_CONTAINER_NAME     Docker container name [default: payment-gateway]"
    echo "  DOCKER_HTTP_PORT          Docker HTTP port mapping [default: 7010]"
    echo "  DOCKER_METRICS_PORT       Docker metrics port mapping [default: 8081]"
    echo "  DOCKER_NETWORK            Docker network name [default: hackload-network]"
    echo "  USE_HOST_NETWORK          Use host network mode [default: false]"
    echo ""
    echo "Examples:"
    echo "  $0                        # Build and run Docker container in development mode"
    echo "  $0 --production           # Build and run Docker container in production mode"
    echo "  $0 --host-network         # Use host network (for local database containers)"
    echo "  $0 --build-only           # Only build Docker image, don't run"
    echo "  $0 --stop                 # Stop running Docker container"
    echo "  $0 --logs                 # Show Docker container logs"
    echo "  $0 --debug                # Show detailed debugging information"
    echo "  BASE_URL=https://api.hackload.kz $0           # Use custom base URL"
    echo "  ADMIN_KEY=my_secure_key $0                    # Use custom admin key"
    echo "  DB_HOST=postgres-container $0                 # Connect to database container by name"
}

# Main function
main() {
    local build_only=false
    local show_logs=false
    local stop_container=false
    local show_debug=false
    
    # Parse command line arguments
    while [[ $# -gt 0 ]]; do
        case $1 in
            --help)
                show_usage
                exit 0
                ;;
            --build-only)
                build_only=true
                shift
                ;;
            --production)
                export ASPNETCORE_ENVIRONMENT="Production"
                shift
                ;;
            --host-network)
                export USE_HOST_NETWORK="true"
                shift
                ;;
            --stop)
                stop_container=true
                shift
                ;;
            --logs)
                show_logs=true
                shift
                ;;
            --debug)
                show_debug=true
                shift
                ;;
            *)
                print_error "Unknown option: $1"
                show_usage
                exit 1
                ;;
        esac
    done
    
    # Handle Docker container management commands
    if [ "$stop_container" = true ]; then
        print_header "Stopping Docker Container"
        if docker ps --format '{{.Names}}' | grep -q "^${DOCKER_CONTAINER_NAME}$"; then
            docker stop "$DOCKER_CONTAINER_NAME"
            docker rm "$DOCKER_CONTAINER_NAME"
            print_success "Container $DOCKER_CONTAINER_NAME stopped and removed"
        else
            print_warning "Container $DOCKER_CONTAINER_NAME is not running"
        fi
        exit 0
    fi
    
    if [ "$show_logs" = true ]; then
        print_header "Showing Docker Container Logs"
        if docker ps -a --format '{{.Names}}' | grep -q "^${DOCKER_CONTAINER_NAME}$"; then
            print_message $BLUE "Following logs for container: $DOCKER_CONTAINER_NAME"
            print_message $BLUE "Press Ctrl+C to stop following logs"
            docker logs "$DOCKER_CONTAINER_NAME" -f
        else
            print_error "Container $DOCKER_CONTAINER_NAME does not exist"
        fi
        exit 0
    fi
    
    if [ "$show_debug" = true ]; then
        print_header "Debugging Container Information"
        if docker ps -a --format '{{.Names}}' | grep -q "^${DOCKER_CONTAINER_NAME}$"; then
            print_message $BLUE "Container Details:"
            docker inspect "$DOCKER_CONTAINER_NAME" --format '{{json .}}' | jq -r '
                "Name: " + .Name,
                "Status: " + .State.Status,
                "Network Mode: " + .HostConfig.NetworkMode,
                "Port Bindings: " + (.HostConfig.PortBindings | tostring),
                "Environment Variables:",
                (.Config.Env[] | select(startswith("ASPNETCORE") or startswith("ConnectionStrings") or startswith("Api__")))
            ' 2>/dev/null || docker inspect "$DOCKER_CONTAINER_NAME"
            
            print_message $BLUE "Container Logs (last 30 lines):"
            docker logs "$DOCKER_CONTAINER_NAME" --tail 30
            
            print_message $BLUE "Container Processes:"
            docker exec "$DOCKER_CONTAINER_NAME" ps aux 2>/dev/null || echo "Cannot access container processes"
            
            print_message $BLUE "Listening Ports in Container:"
            docker exec "$DOCKER_CONTAINER_NAME" ss -tulpn 2>/dev/null || docker exec "$DOCKER_CONTAINER_NAME" netstat -tulpn 2>/dev/null || echo "Cannot check ports"
            
            print_message $BLUE "Host Network Ports:"
            ss -tulpn | grep -E ":(8080|8081|7010)" || echo "No relevant ports found on host"
            
            print_message $BLUE "Testing Container Connectivity:"
            docker exec "$DOCKER_CONTAINER_NAME" curl -v http://localhost:8080 2>/dev/null || echo "Cannot test internal connectivity"
        else
            print_error "Container $DOCKER_CONTAINER_NAME does not exist"
        fi
        exit 0
    fi
    
    # Show banner
    print_header "HackLoad Payment Gateway"
    print_message $GREEN "Version: 1.0"
    print_message $GREEN "Environment: $ASPNETCORE_ENVIRONMENT"
    print_message $GREEN "Build Date: $(date)"
    echo ""
    
    # Execute Docker build process
    check_prerequisites
    create_docker_network
    build_docker_image
    
    if [ "$build_only" = false ]; then
        start_docker_container
    else
        print_success "Docker image built successfully: $DOCKER_IMAGE_NAME:$DOCKER_IMAGE_TAG"
        print_message $BLUE "Use the following commands to run the container:"
        print_message $BLUE "  $0                        # Start the container"
        print_message $BLUE "  docker run -d -p 7010:8080 -p 8081:8081 $DOCKER_IMAGE_NAME:$DOCKER_IMAGE_TAG"
    fi
}

# Run main function with all arguments
main "$@"