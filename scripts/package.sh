#!/usr/bin/env bash
set -euo pipefail

version="${1:-1.4.1}"
jellyfin_version="${2:-10.11.11}"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/.." && pwd)"
archive_name="ChooseYourMeta_${version}_jellyfin-${jellyfin_version}.zip"
artifacts="${repo_root}/artifacts"
publish="${repo_root}/publish"
stage="${artifacts}/package"

if [[ ! "${version}" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Version must use stable semantic versioning, for example 1.3.0." >&2
    exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
    echo ".NET 9 SDK is required." >&2
    exit 1
fi
if ! command -v zip >/dev/null 2>&1; then
    echo "The zip command is required." >&2
    exit 1
fi

for path in "${publish}" "${stage}"; do
    case "${path}" in
        "${repo_root}"/*) rm -rf -- "${path}" ;;
        *)
            echo "Refusing to clean a path outside the repository." >&2
            exit 1
            ;;
    esac
done

mkdir -p -- "${stage}"
dotnet restore \
    "${repo_root}/RussianMetadata.Tests/RussianMetadata.Tests.csproj"
dotnet test \
    "${repo_root}/RussianMetadata.Tests/RussianMetadata.Tests.csproj" \
    -c Release \
    --no-restore \
    -p:Version="${version}"
dotnet publish \
    "${repo_root}/RussianMetadata.csproj" \
    -c Release \
    --no-restore \
    -p:Version="${version}" \
    -o "${publish}"

dll="${publish}/RussianMetadata.dll"
test -f "${dll}"
cp -- "${dll}" "${stage}/"

cat > "${stage}/meta.json" <<EOF
{
  "category": "General",
  "changelog": "Fixed Russian cast and crew localization during large concurrent library scans.",
  "description": "Choose Russian or English metadata, posters, and logos for movies and collections.",
  "guid": "a8f3c2e1-4b5d-6e7f-8a9b-0c1d2e3f4a5b",
  "name": "Choose your Meta!",
  "overview": "Controls RU/EN metadata and artwork without requiring a separate TMDB key.",
  "owner": "Lootfullin",
  "targetAbi": "${jellyfin_version}.0",
  "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "version": "${version}.0",
  "status": "Active",
  "autoUpdate": false
}
EOF

mkdir -p -- "${artifacts}"
archive="${artifacts}/${archive_name}"
rm -f -- "${archive}" "${archive}.sha256"
(
    cd "${stage}"
    zip -q -X "${archive}" RussianMetadata.dll meta.json
)

checksum="$(shasum -a 256 "${archive}" | awk '{print $1}')"
printf '%s  %s\n' "${checksum}" "${archive_name}" > "${archive}.sha256"
printf 'Package: %s\nSHA256: %s\n' "${archive}" "${archive}.sha256"
