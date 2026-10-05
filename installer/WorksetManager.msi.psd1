# AB Worksets Manager - what its .msi installs and where. Built by build\build-installer.ps1 through the
# AB Adv Tools kit (shared\ABAdvTools\msi\New-AdvToolsMsi.ps1); the kit README documents every key.
@{
    # Keys the installer's registry state. Never rename.
    Id          = 'WorksetManager'
    Name        = 'AB Worksets Manager'
    FileName    = 'AB.WorksetManager'
    Repository  = 'AB.WorksetManager.Revit'

    # First release (1.0.0). Constant for the life of the product - never change it.
    UpgradeCode = '2B2015F4-B1DF-46C0-AA00-F8A1AE9B9825'

    Description = 'Workset management for workshared Revit models: create worksets from a list, rename them, put grids, levels, Revit links, CAD and families on the right workset, make workset 3D views, switch workset visibility in views and view templates, and delete worksets after moving their elements. Every change is checked first, leaves alone what other users have borrowed, and is one Undo.'
    NextSteps   = 'Start Revit, open a workshared model and press Worksets Manager on the AB Adv Tools tab.'
    RemovedText = 'AB Worksets Manager has been removed. Your models keep every workset and view it made; your settings and logs are kept.'
    Icon        = 'product.ico'

    # Only me by default: no administrator rights needed, which is what most company PCs allow.
    # Everyone is available for IT deployment.
    Scope        = 'UserOrMachine'
    DefaultScope = 'User'
    ScopeNote    = 'Only me installs into your own profile and needs no administrator rights. Everyone installs for all users into ProgramData and needs administrator rights.'

    RevitAddin = @{
        ManifestFileName  = 'AB.WorksetManager.addin'
        FolderName        = 'ABWorksetManager'
        AssemblyFileName  = 'AB.WorksetManager.Addin.dll'
        AddInName         = 'AB Worksets Manager'
        FullClassName     = 'AB.WorksetManager.Addin.App'
        # Revit keys the registration off this id. Never change it.
        AddInId           = '9c266fe5-1bdb-4e9e-a85b-8633e2c7dfec'
        VendorId          = 'ABLOTFY'
        VendorDescription = 'Abdullah Lotfy - https://www.linkedin.com/in/abdullahalqublawi/'
        # Revit reads only its own year folder, so every build is ticked, installed or not:
        # one package serves a team on mixed versions, including a Revit installed later.
        Releases          = 'AllBuilt'
    }
}
