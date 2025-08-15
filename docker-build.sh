#!/bin/bash

# Docker build script for organizer-app
# Builds, tags with commit SHA, and pushes Docker image

set -e  # Exit on any error

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
REGISTRY="${DOCKER_REGISTRY:-ghcr.io}"
NAMESPACE="${DOCKER_NAMESPACE:-hackload-kz}"
IMAGE_NAME="${DOCKER_IMAGE_NAME:-hackload-paymentgateway}"
DOCKERFILE="${DOCKERFILE:-Dockerfile}"

# Print colored output
print_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Function to check if command exists
command_exists() {
    command -v "$1" >/dev/null 2>&1
}

# Check required tools
check_dependencies() {
    print_info "Checking dependencies..."
    
    if ! command_exists git; then
        print_error "Git is required but not installed"
        exit 1
    fi
    
    if ! command_exists docker; then
        print_error "Docker is required but not installed"
        exit 1
    fi
    
    print_success "All dependencies are available"
}

# Get Git information
get_git_info() {
    print_info "Getting Git information..."
    
    # Check if we're in a git repository
    if ! git rev-parse --git-dir > /dev/null 2>&1; then
        print_error "Not in a Git repository"
        exit 1
    fi
    
    # Get commit SHA (short)
    COMMIT_SHA=$(git rev-parse --short HEAD)
    
    # Get current branch
    BRANCH=$(git rev-parse --abbrev-ref HEAD)
    
    # Check if working directory is clean
    if ! git diff-index --quiet HEAD --; then
        print_warning "Working directory has uncommitted changes"
        DIRTY="-dirty"
    else
        DIRTY=""
    fi
    
    print_info "Branch: ${BRANCH}"
    print_info "Commit SHA: ${COMMIT_SHA}${DIRTY}"
}

# Build Docker image
build_image() {
    print_info "Building Docker image..."
    
    # Full image name with registry
    FULL_IMAGE_NAME="${REGISTRY}/${NAMESPACE}/${IMAGE_NAME}"
    
    # Tags to apply
    TAG_SHA="${COMMIT_SHA}${DIRTY}"
    TAG_LATEST="latest"
    
    print_info "Image name: ${FULL_IMAGE_NAME}"
    print_info "Platform: linux/amd64"
    print_info "Building with tags: ${TAG_SHA}, ${TAG_LATEST}"
    
    # Build with multiple tags for linux/amd64 platform
    docker build \
        --platform linux/amd64 \
        --file "${DOCKERFILE}" \
        --tag "${FULL_IMAGE_NAME}:${TAG_SHA}" \
        --tag "${FULL_IMAGE_NAME}:${TAG_LATEST}" \
        --build-arg BUILDKIT_INLINE_CACHE=1 \
        --build-arg COMMIT_SHA="${COMMIT_SHA}" \
        --build-arg BRANCH="${BRANCH}" \
        . || {
        print_error "Docker build failed"
        exit 1
    }
    
    print_success "Docker image built successfully"
    print_info "Image tags:"
    print_info "  - ${FULL_IMAGE_NAME}:${TAG_SHA}"
    print_info "  - ${FULL_IMAGE_NAME}:${TAG_LATEST}"
}

# Push Docker image
push_image() {
    print_info "Pushing Docker image to registry..."
    
    # Push both tags
    docker push "${FULL_IMAGE_NAME}:${TAG_SHA}" || {
        print_error "Failed to push image with SHA tag"
        exit 1
    }
    
    docker push "${FULL_IMAGE_NAME}:${TAG_LATEST}" || {
        print_error "Failed to push image with latest tag"
        exit 1
    }
    
    print_success "Docker image pushed successfully"
    print_info "Pushed tags:"
    print_info "  - ${FULL_IMAGE_NAME}:${TAG_SHA}"
    print_info "  - ${FULL_IMAGE_NAME}:${TAG_LATEST}"
}

# Show image information
show_image_info() {
    print_info "Image information:"
    docker images "${FULL_IMAGE_NAME}" --format "table {{.Repository}}\t{{.Tag}}\t{{.Size}}\t{{.CreatedAt}}"
}

# Main execution
main() {
    print_info "Starting Docker build process for organizer-app"
    print_info "========================================"
    
    # Parse command line arguments
    PUSH=false
    VERBOSE=false
    
    while [[ $# -gt 0 ]]; do
        case $1 in
            --push)
                PUSH=true
                shift
                ;;
            --verbose|-v)
                VERBOSE=true
                set -x  # Enable verbose output
                shift
                ;;
            --registry)
                REGISTRY="$2"
                shift 2
                ;;
            --namespace)
                NAMESPACE="$2"
                shift 2
                ;;
            --image-name)
                IMAGE_NAME="$2"
                shift 2
                ;;
            --dockerfile)
                DOCKERFILE="$2"
                shift 2
                ;;
            --help|-h)
                echo "Usage: $0 [OPTIONS]"
                echo ""
                echo "Options:"
                echo "  --push              Push image to registry after building"
                echo "  --verbose, -v       Enable verbose output"
                echo "  --registry REG      Docker registry (default: ghcr.io)"
                echo "  --namespace NS      Registry namespace (default: hackload-infra)"
                echo "  --image-name NAME   Image name (default: infra)"
                echo "  --dockerfile FILE   Dockerfile to use (default: Dockerfile)"
                echo "  --help, -h          Show this help message"
                echo ""
                echo "Environment variables:"
                echo "  DOCKER_REGISTRY     Override registry"
                echo "  DOCKER_NAMESPACE    Override namespace"
                echo "  DOCKER_IMAGE_NAME   Override image name"
                echo "  DOCKERFILE          Override dockerfile"
                echo ""
                echo "Examples:"
                echo "  $0                                    # Build only"
                echo "  $0 --push                             # Build and push"
                echo "  $0 --push --registry docker.io        # Use different registry"
                exit 0
                ;;
            *)
                print_error "Unknown option: $1"
                exit 1
                ;;
        esac
    done
    
    # Run the build process
    check_dependencies
    get_git_info
    build_image
    
    if [ "$PUSH" = true ]; then
        push_image
    else
        print_info "Skipping push (use --push to push to registry)"
    fi
    
    show_image_info
    
    print_success "Build process completed!"
    print_info "========================================"
    
    if [ "$PUSH" = true ]; then
        print_info "Your image is now available at:"
        print_info "  ${FULL_IMAGE_NAME}:${TAG_SHA}"
        print_info "  ${FULL_IMAGE_NAME}:${TAG_LATEST}"
    else
        print_info "To push the image, run:"
        print_info "  $0 --push"
        print_info "Or manually:"
        print_info "  docker push ${FULL_IMAGE_NAME}:${TAG_SHA}"
        print_info "  docker push ${FULL_IMAGE_NAME}:${TAG_LATEST}"
    fi
}

# Run main function
main "$@"