#!/usr/bin/env bash
# 使い方:
#   Tools/CompileCheck/check.sh            … 全モジュールをまとめて型チェック
#   Tools/CompileCheck/check.sh Fishing Mining … Core と指定モジュールだけを型チェック（他モジュールに依存していないことの確認）
# ※ Unity Editor でのコンパイル・実行確認の代わりにはならない。
set -euo pipefail
export DOTNET_CLI_UI_LANGUAGE=en   # 日本語環境でも "Build succeeded" で成否を判定できるように
here="$(cd "$(dirname "$0")" && pwd)"
if [ $# -eq 0 ]; then set -- All; fi
name="$(IFS=_; echo "$*")"
dir="$here/_gen/$name"
mkdir -p "$dir"
if [ "$1" = "All" ]; then
  include='<Compile Include="$(ScriptsRoot)/**/*.cs" Exclude="$(ScriptsRoot)/**/Editor/**/*.cs" />'
else
  include='<Compile Include="$(ScriptsRoot)/Core/**/*.cs" Exclude="$(ScriptsRoot)/**/Editor/**/*.cs" />'
  for m in "$@"; do
    [ "$m" = "Core" ] && continue
    include="$include<Compile Include=\"\$(ScriptsRoot)/$m/**/*.cs\" Exclude=\"\$(ScriptsRoot)/**/Editor/**/*.cs\" />"
  done
fi
cat > "$dir/$name.csproj" <<PROJ
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="../../Common.props" />
  <PropertyGroup><AssemblyName>Check.$name</AssemblyName></PropertyGroup>
  <ItemGroup>$include</ItemGroup>
</Project>
PROJ
out="$(dotnet build "$dir/$name.csproj" -nologo 2>&1 || true)"
echo "$out" | grep -E "error CS|warning CS0(108|114|162|618)" | sed "s#$(cd "$here/../.." && pwd)/##" | sort -u | head -${MAXLINES:-200} || true
if echo "$out" | grep -q "Build succeeded"; then
  echo "== OK: $name の型チェック成功 =="
else
  echo "== NG: $name の型チェック失敗（$(echo "$out" | grep -c "error CS" || true) 件のエラー行）=="
  exit 1
fi
