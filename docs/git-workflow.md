# Git setup and workflow

Run these commands in PowerShell from the project folder unless noted otherwise.

## Finish initial setup

Set your commit author identity for this repository. Replace the example values:

```powershell
git config --local user.name "Your Name"
git config --local user.email "your-email@example.com"
git add .gitignore .gitattributes README.md docs/git-workflow.md
git diff --cached
git commit -m "Initialize project repository"
```

Create an empty repository on your chosen host. Leave its README, license, and
gitignore options unchecked because the local repository already has initial files.
Copy its HTTPS clone URL and replace the example URL below:

```powershell
git remote add origin https://github.com/Navy-Chief/Histogram-Inspector.git
git push -u origin main
```

Git Credential Manager handles authentication when prompted. Do not put a password
or token into the remote URL or project files.

## Everyday work

Start a change from an updated main branch:

```powershell
git switch main
git pull
git switch -c feature/histogram
```

Review and commit the intended files; replace the example file path:

```powershell
git status
git diff
git add path/to/changed-file
git diff --cached
git commit -m "Describe the change"
git push -u origin feature/histogram
```

Open a pull request on your hosting service, review it, and merge it. Then:

```powershell
git switch main
git pull
git branch -d feature/histogram
```

The local branch deletion succeeds only when Git considers the work merged. A
squash merge can require a separate check before deleting the branch.

## Clone on another computer

Run from the parent folder where you want a new project copy:

```powershell
git clone https://github.com/Navy-Chief/Histogram-Inspector.git
cd Histogram-Inspector
git config --local user.name "Your Name"
git config --local user.email "your-email@example.com"
```

Clone creates the folder, downloads history, and configures origin. Do not clone
into this already initialized project folder.

## Useful checks and recovery

```powershell
git status                       # Current branch and changed files
git remote -v                    # Fetch and push destinations
git log --oneline --graph -10     # Recent history
git fetch                        # Download remote updates without changing files
git diff --cached                # Review what the next commit will contain
git restore --staged path/to/file # Unstage a file while keeping its edits
git stash push -m "Work in progress" # Temporarily save tracked edits
git stash pop                    # Restore the latest stash; resolve conflicts if any
git revert COMMIT_HASH           # Create a new commit undoing an earlier commit
```

Stash does not include untracked files unless you add `-u`.

This repository uses fast-forward-only pulls. If a pull reports diverged history,
run `git fetch`, inspect the history, and decide how to integrate the branches.
For an unpublished local commit, rebasing onto the upstream branch can be suitable;
coordinate before rewriting any shared history.

## Remote repository settings

Once the remote exists, consider requiring pull requests and successful build
checks before merging into main. Add the build workflow after choosing the project
SDK and build command. Choose a license before distributing the project publicly.
