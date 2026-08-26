# Preset — .NET WPF App

- .NET 10, MVVM via CommunityToolkit.Mvvm source generators
  (`[ObservableProperty]`, `[RelayCommand]`) — hand-written observable
  boilerplate is a smell.
- Composition root: a `Microsoft.Extensions.DependencyInjection`
  ServiceProvider built in `App`. Views, viewmodels, and services are
  registered and resolved there — code-behind never `new`s a dependency.
- Feature folders keep view + viewmodel + services together; shared controls
  move into a `<Product>.Controls` project when the second consumer appears.
- Tests: xUnit on viewmodels (unit focus); UI automation covers key processes
  only.
- Dependencies: licence rule and audit live in the `abhaengigkeiten` skill.
