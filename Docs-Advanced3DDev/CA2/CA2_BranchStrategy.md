# CA2 Branch Strategy

All CA2 networking work was done on a dedicated `feature/ca2-network` branch created in Week 7. This kept the networking code isolated from `main`, which was kept clean and working at all times. Before submission the branch is merged back into `main` and the `ca2-submit` tag applied.

Committing directly to `main` in the earlier weeks meant the history had no branching and was harder to read. Creating the feature branch in Week 7 made the networking work clearly separated and the eventual merge and tag much cleaner. For CA3 I'll use feature branches from the start.
