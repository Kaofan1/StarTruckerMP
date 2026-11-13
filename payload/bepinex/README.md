# BepInEx payload (optional)

Ten folder jest **osobny** od launchera i od pluginu. Launcher instaluje się i działa bez niego -
BepInEx jest czytany tylko wtedy, gdy klikniesz **Zainstaluj**.

## Jak używać

Wrzuć tutaj rozpakowane pliki **BepInEx 6 (IL2CPP, win-x64)**:

```
payload/bepinex/
  winhttp.dll
  doorstop_config.ini
  .doorstop_version
  BepInEx/...
  dotnet/...
```

Możesz też wrzucić pojedynczy plik `.zip` z tym buildem - launcher rozpakuje go sam.

Po instalacji launchera ten folder znajduje się w
`%LOCALAPPDATA%\StarTruckerMP\payload\bepinex`.

## Gdy folder jest pusty

Launcher pobiera najnowszy build IL2CPP z
[BepInEx Bleeding Edge](https://builds.bepinex.dev/projects/bepinex_be), więc payload jest
wyłącznie wygodą offline (instalacja bez internetu albo przypięta, znana wersja loadera).

> Pliki BepInEx nie są commitowane do repozytorium - ten folder jest w `.gitignore`.
