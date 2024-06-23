# PowerShell script to automate Git workflow

# Get the user's current directory
$parentDir = Get-Location

# Process each repository
Get-ChildItem -Path $parentDir -Recurse -Directory | Where-Object { Test-Path (Join-Path $_.FullName '.git') } |
ForEach-Object {
    try {
        $repoPath = $_.FullName
        $repoName = Split-Path -Leaf $repoPath
        Write-Host "Processing: $repoPath"
        
        # Change directory to the repository
        Push-Location $repoPath
        
        # Get the origin URL
        $originUrl = git config --get remote.origin.url
        
        # Extract the base URL for upstream
        $upstreamUrl = $originUrl -replace '^(https:\/\/[^\/]+\/)([^\/]+\/)', '${1}LiquidMovz/'
        
        # Add upstream if it doesn't exist
        if (!(git remote | findstr upstream)) {
            Write-Host "Adding upstream..."
            git remote add upstream $upstreamUrl
        }

        # Fetch from upstream
        git fetch upstream
        
        # Determine the default branch (main or master)
        $defaultBranch = 'main'
        if (!(git show-ref refs/heads/main)) {
            $defaultBranch = 'master'
        }
        
        # Checkout the default branch
        git checkout $defaultBranch
        
        # Merge upstream/defaultBranch into local defaultBranch
        git merge "upstream/$defaultBranch" -m "Merge updates from upstream"
        
        # If you have local changes, you can commit them here
        # git add .
        # git commit -m "Your commit message"
        
        # Push changes to your origin (forked repo)
        git push origin $defaultBranch
        
        # Pop the directory to go back up one level
        Pop-Location
    } catch {
        Write-Error $_
    }
}
