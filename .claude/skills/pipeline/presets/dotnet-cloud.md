# Preset — .NET Cloud App

Server: ASP.NET Core Minimal API on .NET 10. Architecture: vertical slices.

## Slice layout

- Each feature is a csproj pair: `<Product>.Core.<Feature>` (implementation)
  and `<Product>.Core.<Feature>.Contracts`. Namespaces follow the project
  names — C# identifiers allow no hyphens (`WeddingPlaner`, never
  `Wedding-Planer`; hyphens live only in repo/folder names).
- Features reference each other only through `.Contracts`; only the host
  (`<Product>.Server`) references implementation projects.
- Every implementation project exposes one `IServiceCollection`
  `Add<Feature>()` extension registering its services, so its types stay
  `internal`. Boundary violations are compile errors — that is the point.

## Client — Blazor WebAssembly

- The client references the `.Contracts` projects directly: typed API calls
  without code generation.
- Feature folders mirror the server slices.
- Component library: a Razor Class Library `<Product>.Components`, grown one
  control at a time when a screen needs it — never speculatively.
- CSS: the global stylesheet holds only the design tokens from
  `design-system.md`; everything else lives in per-component CSS isolation
  (`.razor.css`).

## Tests — xUnit

Categories: `Core` (unit), `Server.Integration`, `Client.UI` (bUnit), `E2E`
(Playwright). Weight lies on fast unit tests; E2E covers key processes only.
Create each test project with its first real test.
- Dependencies: licence rule and audit live in the `abhaengigkeiten` skill.

## HTTP edge cases

A handler that intercepts a status code must know the case where that code is
the right answer, or it inverts its meaning — a 401 answering "is anyone signed
in?" is not an expired session. And "no success" has two shapes, a status code
and an exception: a stub returning only status codes tests one of them.

## Blazor WebAssembly — two silent starts

Both fail without a compiler error and without a failing test; only running the
app in a browser shows them.

- `index.html` must link `<Product>.styles.css` beside the global stylesheet.
  Without it every `.razor.css` is dead weight: components render unstyled while
  the global sheet still applies, so the page looks plausible and is wrong.
- A non-invariant culture needs
  `<BlazorWebAssemblyLoadAllGlobalizationData>true</...>` in the client csproj,
  or `new CultureInfo("de-DE")` throws during startup and the app never boots.

Native `<input type="date">` renders its placeholder in the *browser's*
language, not the page culture — screenshot tooling must launch the browser
with `--lang`, or German docs show `mm/dd/yyyy`.
