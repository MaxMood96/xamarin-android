# How to branch for .NET releases

Context: [dotnet/maui#589][0]

Microsoft employees can review the [.NET schedule][4] for upcoming
releases. We normally branch on during the "Code complete, release
branch snap and build" window. Sometimes we also wait until a newly
branded .NET version appears in Maestro updates to `main`.

Let's say that it's time for a hypothetical ".NET 10 Preview 42". The
sequence of events would be:

1. [dotnet/dotnet][1] branches `release/10.0.1xx-preview42`

2. Builds are available on Maestro for `dotnet/android` to consume.

3. `dotnet/android` branches `release/10.0.1xx-preview42`. GitHub Web
   UI is fine for this.

4. Subscribe to Maestro updates for [dotnet/dotnet][1] `release/10.0.1xx-preview42`:

```bash
darc add-subscription --channel ".NET 10.0.1xx SDK Preview 42" --target-branch "release/10.0.1xx-preview42" --source-repo https://github.com/dotnet/dotnet --target-repo https://github.com/dotnet/android
```

5. Publish Maestro updates for `dotnet/android/release/10.0.1xx-preview42`:

```bash
darc add-default-channel --channel ".NET 10.0.1xx SDK Preview 42" --branch "release/10.0.1xx-preview42" --repo https://github.com/dotnet/android
```

See [eng/README.md][2] for details on `darc` commands.

6. Open a PR to `dotnet/android/main`, such that
   `$(AndroidPackVersionSuffix)` in `Directory.Build.props` is
   incremented to the *next* version: `preview.43`. You may also need
   to update `$(AndroidPackVersion)` if `main` needs to target a new
   .NET version band.

Note that release candidates will use values such as `rc.1`, `rc.2`, etc.

7. Update the [Xamarin.Android Nightly job][3], so the schedule only
   runs on desired branches. We likely only need a single .NET 10+
   branch to be on this schedule at a time.

8. When the build is complete, verify the version number is as
   expected (36.0.0-preview.42.x) and that builds show up on
   https://maestro.dot.net/ in the appropriate "channel" for
   dotnet/maui to consume.

[0]: https://github.com/dotnet/maui/issues/598
[1]: https://github.com/dotnet/dotnet
[2]: ../../eng/README.md
[3]: https://devdiv.visualstudio.com/DevDiv/_apps/hub/ms.vss-ciworkflow.build-ci-hub?_a=edit-build-definition&id=14072&view=Tab_Triggers
[4]: https://aka.ms/net10-schedule
