#!/bin/sh
# Archive unsigned, then sign locally, following FreeTheNotch's release workflow.
# Keep the same certificate and private key for subsequent releases.
# LINKPEEK_SIGNING_IDENTITY overrides "LinkPeek Release"; "-" is for local tests.
set -eu

IDENTITY="${LINKPEEK_SIGNING_IDENTITY:-LinkPeek Release}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUTPUT="$ROOT/build/release"

fail() {
    echo "error: $*" >&2
    exit 1
}

if [ "$IDENTITY" != "-" ]; then
    security find-identity -p codesigning | grep -Fq "\"$IDENTITY\"" ||
        fail "no code-signing identity named \"$IDENTITY\" in Keychain; import your certificate and private key using Keychain Access"
fi

mkdir -p "$OUTPUT"
# Keep previous archives intact and never delete user-supplied signing material.
WORK="$(mktemp -d "$OUTPUT/archive.XXXXXX")"
ARCHIVE="$WORK/LinkPeek.xcarchive"

echo "==> Archiving Release build"
xcodebuild -project "$ROOT/LinkPeek.xcodeproj" -scheme LinkPeek -configuration Release \
    -archivePath "$ARCHIVE" archive CODE_SIGNING_ALLOWED=NO -quiet

APP="$ARCHIVE/Products/Applications/LinkPeek.app"
[ -d "$APP" ] || fail "LinkPeek.app not found in archive"

echo "==> Signing with \"$IDENTITY\""
# Sign embedded libraries first. LinkPeek currently needs no custom entitlements.
find "$APP/Contents" -name '*.dylib' -type f -exec \
    codesign --force --timestamp=none --sign "$IDENTITY" {} \;
codesign --force --timestamp=none --options runtime --sign "$IDENTITY" "$APP"
codesign --verify --strict --deep "$APP" || fail "signature does not verify"

echo "==> Checking for personal data"
if grep -rlaF "$HOME/" "$APP" >/dev/null; then
    fail "the app contains paths from this Mac"
fi
SIGNATURE="$(codesign -dvv "$APP" 2>&1)" || fail "cannot inspect signing metadata"
if printf '%s\n' "$SIGNATURE" | grep '^Authority=' | grep -q '@'; then
    fail "the signing certificate contains an email address"
fi

VERSION="$(plutil -extract CFBundleShortVersionString raw "$APP/Contents/Info.plist")"
BUILD="$(plutil -extract CFBundleVersion raw "$APP/Contents/Info.plist")"
case "$VERSION" in
    ''|*[!0-9.]*) fail "invalid marketing version in built app" ;;
esac
SUFFIX=""
if [ "$IDENTITY" = "-" ]; then SUFFIX="-adhoc"; fi
NAME="LinkPeek-$VERSION$SUFFIX.zip"

echo "==> Packaging"
ditto -c -k --sequesterRsrc --keepParent "$APP" "$WORK/$NAME"
(cd "$WORK" && shasum -a 256 "$NAME" >"$NAME.sha256")
# Publish artifacts only after all signing and privacy checks succeed.
mv "$WORK/$NAME" "$OUTPUT/$NAME"
mv "$WORK/$NAME.sha256" "$OUTPUT/$NAME.sha256"

echo
echo "Version $VERSION (build-$BUILD)"
echo "App:     $APP"
echo "Zip:     $OUTPUT/$NAME"
echo "SHA-256: $(cut -d ' ' -f 1 "$OUTPUT/$NAME.sha256")"
if [ "$IDENTITY" = "-" ]; then
    echo "Local ad-hoc test build; not signed with the release identity."
else
    echo "This app is locally signed, not notarized by Apple."
fi
