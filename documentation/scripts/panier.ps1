#Requires -Version 7.0
<#
.SYNOPSIS
    Fonctions d'appel de l'API panier pour les tests manuels.

.DESCRIPTION
    Mode opératoire : documentation/tests-manuels-panier.md.
    À charger dans chaque terminal PowerShell 7, à la racine du dépôt :

        . ./documentation/scripts/panier.ps1

    Chaque appel affiche un résumé lisible (statut, panier ou erreur) et renvoie un objet
    { Status, Body } : l'affecter à une variable pour réutiliser les identifiants et versions.

    Instance 1 : http://localhost:5080 (profil « Start API »).
    Instance 2 : http://localhost:5081 (seconde instance, voir le mode opératoire).
#>

$BeanStockInstances = @{
    1 = 'http://localhost:5080'
    2 = 'http://localhost:5081'
}

function Invoke-BeanStock {
    <#
    .SYNOPSIS
        Envoie une requête à l'API et affiche le résultat. Les statuts d'erreur ne lèvent pas d'exception.
    .PARAMETER User
        Utilisateur courant (en-tête X-User-Id). Chaîne vide : requête sans en-tête.
    #>
    param(
        [Parameter(Mandatory)][ValidateSet('GET', 'POST', 'PUT', 'DELETE')][string] $Method,
        [Parameter(Mandatory)][string] $Path,
        [hashtable] $Body,
        [AllowEmptyString()][string] $User = 'alice',
        [ValidateSet(1, 2)][int] $Instance = 1
    )

    $request = @{
        Method             = $Method
        Uri                = "$($BeanStockInstances[$Instance])$Path"
        SkipHttpErrorCheck = $true
    }
    if ($User) { $request.Headers = @{ 'X-User-Id' = $User } }
    if ($Body) {
        $request.Body = $Body | ConvertTo-Json
        $request.ContentType = 'application/json'
    }

    $response = Invoke-WebRequest @request
    $content = if ($response.Content) { $response.Content | ConvertFrom-Json } else { $null }
    $result = [pscustomobject]@{ Status = [int]$response.StatusCode; Body = $content }

    Write-BeanStockResult $result
    $result
}

function Write-BeanStockResult {
    param([Parameter(Mandatory)] $Result)

    $color = if ($Result.Status -lt 300) { 'Green' } elseif ($Result.Status -eq 409) { 'Yellow' } else { 'Red' }
    Write-Host ('{0} {1}' -f $Result.Status, [System.Net.HttpStatusCode]$Result.Status) -ForegroundColor $color

    $body = $Result.Body
    if ($null -eq $body) { return }
    $properties = $body.PSObject.Properties.Name

    if ($properties -contains 'lines') {
        Write-Host ('  Panier v{0} · total {1:N2} · {2} ligne(s)' -f $body.version, $body.total, @($body.lines).Count)
        foreach ($line in $body.lines) {
            Write-Host ('  - {0}  {1}  x{2} à {3:N2} = {4:N2}  (version {5})' -f
                $line.lineId, $line.productName, $line.quantity, $line.unitPrice, $line.subtotal, $line.version)
        }
    }
    elseif ($properties -contains 'title') {
        $code = if ($body.code) { $body.code } else { '-' }
        Write-Host ('  {0} : {1}' -f $code, $body.title)
        if ($body.detail) { Write-Host "  $($body.detail)" }
        if ($body.errors) {
            foreach ($field in $body.errors.PSObject.Properties) {
                Write-Host ('  {0} : {1}' -f $field.Name, ($field.Value -join ' '))
            }
        }
        foreach ($name in 'expectedVersion', 'currentVersion', 'currentQuantity', 'requestedQuantity', 'max') {
            if ($null -ne $body.$name) { Write-Host "  $name = $($body.$name)" }
        }
    }
}

function Get-Products {
    <# .SYNOPSIS Premiers produits actifs du catalogue local (identifiant local à utiliser dans le panier). #>
    param([int] $Count = 5)

    $page = Invoke-RestMethod "$($BeanStockInstances[1])/products?pageSize=$Count"
    $page.items | Select-Object id, externalId, name, price
}

function New-Cart {
    <# .SYNOPSIS Crée le panier de l'utilisateur, ou le renvoie s'il existe (POST /me/cart). #>
    param([string] $User = 'alice', [ValidateSet(1, 2)][int] $Instance = 1)

    Invoke-BeanStock POST '/me/cart' -User $User -Instance $Instance
}

function Get-Cart {
    <# .SYNOPSIS Consulte le panier de l'utilisateur (GET /me/cart). #>
    param([string] $User = 'alice', [ValidateSet(1, 2)][int] $Instance = 1)

    Invoke-BeanStock GET '/me/cart' -User $User -Instance $Instance
}

function Add-CartLine {
    <# .SYNOPSIS Ajoute un produit, ou augmente sa quantité s'il est déjà dans le panier (POST /me/cart/lines). #>
    param(
        [Parameter(Mandatory)][string] $ProductId,
        [int] $Quantity = 1,
        [string] $User = 'alice',
        [ValidateSet(1, 2)][int] $Instance = 1
    )

    Invoke-BeanStock POST '/me/cart/lines' -Body @{ productId = $ProductId; quantity = $Quantity } -User $User -Instance $Instance
}

function Set-CartLineQuantity {
    <# .SYNOPSIS Remplace la quantité d'une ligne (PUT /me/cart/lines/{lineId}). Sans -Version, la version n'est pas envoyée. #>
    param(
        [Parameter(Mandatory)][string] $LineId,
        [Parameter(Mandatory)][int] $Quantity,
        [Nullable[int]] $Version,
        [string] $User = 'alice',
        [ValidateSet(1, 2)][int] $Instance = 1
    )

    $body = @{ quantity = $Quantity }
    if ($null -ne $Version) { $body.version = $Version }
    Invoke-BeanStock PUT "/me/cart/lines/$LineId" -Body $body -User $User -Instance $Instance
}

function Remove-CartLine {
    <# .SYNOPSIS Supprime une ligne (DELETE /me/cart/lines/{lineId}?version=…). Sans -Version, la version n'est pas envoyée. #>
    param(
        [Parameter(Mandatory)][string] $LineId,
        [Nullable[int]] $Version,
        [string] $User = 'alice',
        [ValidateSet(1, 2)][int] $Instance = 1
    )

    $path = "/me/cart/lines/$LineId"
    if ($null -ne $Version) { $path += "?version=$Version" }
    Invoke-BeanStock DELETE $path -User $User -Instance $Instance
}

function Invoke-BeanStockParallel {
    <#
    .SYNOPSIS
        Envoie la même requête plusieurs fois en parallèle, en alternant les deux instances, puis compte les statuts.
    .PARAMETER SingleInstance
        N'utilise que l'instance 1 (quand la seconde n'est pas lancée).
    #>
    param(
        [Parameter(Mandatory)][ValidateSet('POST', 'PUT', 'DELETE')][string] $Method,
        [Parameter(Mandatory)][string] $Path,
        [hashtable] $Body,
        [string] $User = 'alice',
        [int] $Count = 10,
        [switch] $SingleInstance
    )

    $urls = $BeanStockInstances
    $single = $SingleInstance.IsPresent
    $json = if ($Body) { $Body | ConvertTo-Json } else { $null }

    $statuses = 1..$Count | ForEach-Object -ThrottleLimit $Count -Parallel {
        $instance = if ($using:single) { 1 } else { ($_ % 2) + 1 }
        $request = @{
            Method             = $using:Method
            Uri                = "$(($using:urls)[$instance])$($using:Path)"
            Headers            = @{ 'X-User-Id' = $using:User }
            SkipHttpErrorCheck = $true
        }
        if ($using:json) {
            $request.Body = $using:json
            $request.ContentType = 'application/json'
        }
        [int](Invoke-WebRequest @request).StatusCode
    }

    $statuses | Group-Object | Sort-Object Name | ForEach-Object {
        Write-Host ('  {0} × {1} {2}' -f $_.Count, $_.Name, [System.Net.HttpStatusCode][int]$_.Name)
    }
}

Write-Host 'Fonctions du panier chargées : Get-Products, New-Cart, Get-Cart, Add-CartLine, Set-CartLineQuantity, Remove-CartLine, Invoke-BeanStockParallel.' -ForegroundColor Cyan
