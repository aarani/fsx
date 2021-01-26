#!/usr/bin/env bash
set -e

if ! which make >/dev/null 2>&1; then
    echo "checking for make... not found"
    exit 1
else
    echo "checking for make... found"
fi

if ! which fsharpc >/dev/null 2>&1; then
    echo "checking for F# compiler... not found"
    exit 1
else
    echo "checking for F# compiler... found"
fi

BUILDTOOL=invalid
if ! which msbuild >/dev/null 2>&1; then
    echo "checking for msbuild... not found"

    if ! which xbuild >/dev/null 2>&1; then
        echo "checking for xbuild... not found"
        exit 1
    else
        echo "checking for xbuild... found"
        BUILDTOOL=xbuild
    fi
else
    echo "checking for msbuild... found"
    BUILDTOOL=msbuild
fi

DESCRIPTION="tarball"
if which git >/dev/null 2>&1; then
    # https://stackoverflow.com/a/12142066/1623521
    DESCRIPTION=`git rev-parse --abbrev-ref HEAD`
fi

#default:
PREFIX=/usr/local

for i in "$@"
do
case $i in
    -p=*|--prefix=*)
    PREFIX="${i#*=}"

    ;;
    *)
            # unknown option
    ;;
esac
done

source version.config
echo -e "BuildTool=$BUILDTOOL\nPrefix=$PREFIX" > build.config

echo
echo -e "\tConfiguration summary for fsx $Version ($DESCRIPTION)"
echo
echo -e "\t* Installation prefix: $PREFIX"
echo
