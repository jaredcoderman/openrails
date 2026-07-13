# GitHub Pages Setup - Complete

## Files Created

### 1. GitHub Actions Workflow
**File**: `.github/workflows/docs.yml`

Automatically builds and deploys documentation when you push changes to:
- `track_builder_docs/` folder
- `mkdocs.yml` file
- `.github/workflows/docs.yml` file

**What it does:**
1. Triggers on push to `main` branch
2. Installs Python and mkdocs
3. Builds HTML site with `mkdocs build`
4. Deploys to `gh-pages` branch
5. GitHub Pages serves it automatically

### 2. Main README
**File**: `README.md` (at repo root)

Links to the documentation with:
- Badge showing docs link
- Quick start section
- File formats reference
- Full documentation link
- Tool overview

**Links point to**: `https://jaredt82.github.io/openrails/`

## Setup Instructions

### Step 1: Enable GitHub Pages

1. Go to repository → **Settings** → **Pages**
2. Under "Source", select:
   - Branch: `gh-pages`
   - Folder: `/ (root)`
3. Click **Save**

*Note: The `gh-pages` branch will be created automatically by GitHub Actions*

### Step 2: Push Changes

```bash
git add .github/workflows/docs.yml README.md
git commit -m "Add GitHub Pages documentation deployment"
git push origin main
```

### Step 3: Verify Deployment

1. Go to repo → **Actions** tab
2. Look for "Build and Deploy Documentation" workflow
3. Wait for it to complete (green checkmark)
4. Visit: `https://jaredt82.github.io/openrails/`

**First deployment takes ~1-2 minutes**

## How It Works

### On Every Push

When you push changes to the `track_builder_docs/` folder:

```
Your push
    ↓
GitHub Actions triggers
    ↓
Builds mkdocs site (HTML)
    ↓
Pushes to gh-pages branch
    ↓
GitHub Pages serves it
    ↓
Live at https://jaredt82.github.io/openrails/
```

### URL Structure

```
Repository: openrails
Username: jaredt82

Documentation URL: https://jaredt82.github.io/openrails/

Pages:
- Home: https://jaredt82.github.io/openrails/
- Quick Start: https://jaredt82.github.io/openrails/quick_start/
- File Formats: https://jaredt82.github.io/openrails/formats/
- Full Walkthrough: https://jaredt82.github.io/openrails/pipeline/full_walkthrough/
```

## Local Testing Before Pushing

To test locally before pushing:

```bash
cd C:\Users\jared\main\openrails

# Create/activate venv
python -m venv venv
.\venv\Scripts\Activate.ps1

# Install mkdocs
pip install mkdocs mkdocs-material

# Serve locally
mkdocs serve
```

Then visit `http://localhost:8000` in your browser.

## Troubleshooting

### Documentation not appearing

1. **Check workflow status**
   - Go to Actions tab
   - Look for "Build and Deploy Documentation"
   - If red X: click to see error logs

2. **Check GitHub Pages settings**
   - Settings → Pages
   - Ensure source is `gh-pages` branch
   - Should show "Your site is published at..."

3. **Wait for DNS**
   - Sometimes takes 1-2 minutes
   - Try incognito/private browser window
   - Clear browser cache

### Build fails

Check the workflow logs in Actions tab:
- Look for Python/mkdocs errors
- Usually means syntax error in markdown files
- Check `mkdocs.yml` indentation

### 404 Errors

1. Check mkdocs.yml navigation paths are correct
2. Verify file names match exactly (case sensitive)
3. Look at site build output for errors

## Updating Documentation

### Simple Update

```bash
# Edit any file in track_builder_docs/
vim track_builder_docs/quick_start.md

# Commit and push
git add track_builder_docs/quick_start.md
git commit -m "Update quick start guide"
git push origin main

# GitHub Actions automatically rebuilds!
# ~2 minutes later, changes are live
```

### No Manual Build Needed

Previously, you'd need to:
```bash
mkdocs build
git add site/
git push
```

**Now you don't!** Just edit and push - GitHub Actions handles it.

## File Changes Made

```
.github/workflows/docs.yml    [NEW]  GitHub Actions workflow
README.md                     [NEW]  Main repository README with links
mkdocs.yml                    [UNCHANGED] Navigation already set up
track_builder_docs/           [UNCHANGED] Documentation content
```

## Next Steps

1. **Push changes to GitHub**
   ```bash
   git add .
   git commit -m "Setup GitHub Pages documentation"
   git push origin main
   ```

2. **Enable GitHub Pages** (Settings → Pages)

3. **Watch Actions** (Actions tab - should see workflow running)

4. **Visit your docs** (https://jaredt82.github.io/openrails/)

5. **Share the link!** Now anyone can read your documentation without running mkdocs locally

## Documentation is Now

- ✅ **Automated**: Builds on every push
- ✅ **Hosted**: On GitHub Pages
- ✅ **Discoverable**: Linked from main README
- ✅ **Current**: Always up-to-date with your code
- ✅ **Accessible**: No installation needed to read

---

**Your documentation pipeline is complete!**

- Python Curve Fitter → primitives.json
- TdbDump → track files
- Track files → Open Rails
- Documentation → GitHub Pages ✓
