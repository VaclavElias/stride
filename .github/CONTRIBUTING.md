# 🤝 Contributing

For questions and general discussions, please join our [Discord server](https://discord.gg/f6aerfE) or participate in [GitHub Discussions](https://github.com/stride3d/stride/discussions).

To report bugs or propose features, please use the [Issues](https://github.com/stride3d/stride/issues) section on GitHub.

We welcome code contributions via pull requests. Issues tagged with **[`good first issue`](https://github.com/stride3d/stride/labels/good%20first%20issue)** are great starting points for code contributions.

You can help us translate Stride; check out our [Localization Guide](https://doc.stride3d.net/latest/en/contributors/engine/localization.html).

## Contributor acknowledgements

Maintainers can update `.all-contributorsrc` and regenerate the `README.md` contributor table with the repository-local .NET 10 file-based tool:

```bash
dotnet run build/AllContributors.cs -- sync --repo-root .
```

To apply one or more structured contributor commands locally, place them in a file and run:

```bash
dotnet run build/AllContributors.cs -- apply-commands --repo-root . --commands-file /tmp/all-contributors.txt
```

Supported commands are explicit and line-based:

```text
/contributor add @user1 code
/contributor add @user2 doc
```

The **All Contributors** workflow accepts the same multiline input through **Run workflow**, and it also listens for maintainer issue comments with the same syntax. Successful runs open a pull request from an `all-contributors/add-Username` branch.

## Earn Money by Contributing

If you are a developer with solid experience in C#, rendering techniques, or game development, we want to hire you! We have allocated funds from supporters on [Open Collective](https://opencollective.com/stride3d) and can pay for work on certain projects. [More information is available here](https://doc.stride3d.net/latest/en/contributors/engine/bug-bounties.html).
