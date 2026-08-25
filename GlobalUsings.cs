// 发布构建（tML 的 ModSources 编译）不应用 csproj 的 ImplicitUsings，
// 这里显式补齐常用命名空间，保证发布构建与本地 build 一致（见 03-build-test-publish.md）。
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
