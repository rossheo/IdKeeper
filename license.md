MIT License

Copyright (c) 2026 RossHeo

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

---

## Third-Party Notices

IdKeeper uses third-party NuGet packages. Each package remains subject to its
respective license terms; refer to each package's page for the applicable
license and notices.

The NuGet list below is generated from the solution's top-level packages as
reported by `dotnet list IdKeeper.slnx package` (transitive dependencies are
not enumerated here).

### NuGet packages (direct references)
- `Asp.Versioning.Mvc` — <https://www.nuget.org/packages/Asp.Versioning.Mvc>
- `Asp.Versioning.Mvc.ApiExplorer` — <https://www.nuget.org/packages/Asp.Versioning.Mvc.ApiExplorer>
- `Aspire.Hosting.Redis` — <https://www.nuget.org/packages/Aspire.Hosting.Redis>
- `Aspire.Hosting.Seq` — <https://www.nuget.org/packages/Aspire.Hosting.Seq>
- `Aspire.Seq` — <https://www.nuget.org/packages/Aspire.Seq>
- `Aspire.StackExchange.Redis` — <https://www.nuget.org/packages/Aspire.StackExchange.Redis>
- `Extensions.MudBlazor.StaticInput` — <https://www.nuget.org/packages/Extensions.MudBlazor.StaticInput>
- `FluentValidation.AspNetCore` — <https://www.nuget.org/packages/FluentValidation.AspNetCore>
- `Google.Protobuf` — <https://www.nuget.org/packages/Google.Protobuf>
- `IdGen` — <https://www.nuget.org/packages/IdGen>
- `Microsoft.AspNetCore.DataProtection.Abstractions` — <https://www.nuget.org/packages/Microsoft.AspNetCore.DataProtection.Abstractions>
- `Microsoft.AspNetCore.DataProtection.StackExchangeRedis` — <https://www.nuget.org/packages/Microsoft.AspNetCore.DataProtection.StackExchangeRedis>
- `Microsoft.AspNetCore.OpenApi` — <https://www.nuget.org/packages/Microsoft.AspNetCore.OpenApi>
- `Microsoft.Extensions.Hosting` — <https://www.nuget.org/packages/Microsoft.Extensions.Hosting>
- `Microsoft.Extensions.Http.Resilience` — <https://www.nuget.org/packages/Microsoft.Extensions.Http.Resilience>
- `Microsoft.Extensions.Identity.Stores` — <https://www.nuget.org/packages/Microsoft.Extensions.Identity.Stores>
- `Microsoft.Extensions.ServiceDiscovery` — <https://www.nuget.org/packages/Microsoft.Extensions.ServiceDiscovery>
- `Microsoft.NET.Test.Sdk` — <https://www.nuget.org/packages/Microsoft.NET.Test.Sdk>
- `MudBlazor` — <https://www.nuget.org/packages/MudBlazor>
- `OpenTelemetry.Exporter.OpenTelemetryProtocol` — <https://www.nuget.org/packages/OpenTelemetry.Exporter.OpenTelemetryProtocol>
- `OpenTelemetry.Extensions.Hosting` — <https://www.nuget.org/packages/OpenTelemetry.Extensions.Hosting>
- `OpenTelemetry.Instrumentation.AspNetCore` — <https://www.nuget.org/packages/OpenTelemetry.Instrumentation.AspNetCore>
- `OpenTelemetry.Instrumentation.Http` — <https://www.nuget.org/packages/OpenTelemetry.Instrumentation.Http>
- `OpenTelemetry.Instrumentation.Runtime` — <https://www.nuget.org/packages/OpenTelemetry.Instrumentation.Runtime>
- `SimpleBase` — <https://www.nuget.org/packages/SimpleBase>
- `StackExchange.Redis` — <https://www.nuget.org/packages/StackExchange.Redis>
- `Swashbuckle.AspNetCore.SwaggerUI` — <https://www.nuget.org/packages/Swashbuckle.AspNetCore.SwaggerUI>
- `TickerQ` — <https://www.nuget.org/packages/TickerQ>
- `xunit` — <https://www.nuget.org/packages/xunit>
- `xunit.runner.visualstudio` — <https://www.nuget.org/packages/xunit.runner.visualstudio>

### Auto-referenced NuGet packages
Some SDK tooling adds packages automatically (marked as auto-referenced by the CLI).

- `Aspire.Dashboard.Sdk.win-x64` — <https://www.nuget.org/packages/Aspire.Dashboard.Sdk.win-x64>
- `Aspire.Hosting.AppHost` — <https://www.nuget.org/packages/Aspire.Hosting.AppHost>
- `Aspire.Hosting.Orchestration.win-x64` — <https://www.nuget.org/packages/Aspire.Hosting.Orchestration.win-x64>
- `Microsoft.AspNetCore.App.Internal.Assets` — <https://www.nuget.org/packages/Microsoft.AspNetCore.App.Internal.Assets>

### Static web assets
`IdKeeper.Web/wwwroot/lib/` bundles third-party static web assets (Bootstrap). The
corresponding license file is bundled alongside those assets.

### Adapted source code
`IdKeeper.SnowflakeApiService/SegmentedIntegers/BlockedInteger*.cs` is adapted from
[rossheo/SegmentedInteger](https://github.com/rossheo/SegmentedInteger), licensed under
the BSD 3-Clause License:

> BSD 3-Clause License
>
> Copyright (c) 2024, Ross Heo
>
> Redistribution and use in source and binary forms, with or without
> modification, are permitted provided that the following conditions are met:
>
> 1. Redistributions of source code must retain the above copyright notice, this
>    list of conditions and the following disclaimer.
>
> 2. Redistributions in binary form must reproduce the above copyright notice,
>    this list of conditions and the following disclaimer in the documentation
>    and/or other materials provided with the distribution.
>
> 3. Neither the name of the copyright holder nor the names of its
>    contributors may be used to endorse or promote products derived from
>    this software without specific prior written permission.
>
> THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
> AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
> IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
> ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE
> LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
> CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
> SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
> INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
> CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
> ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
> POSSIBILITY OF SUCH DAMAGE.