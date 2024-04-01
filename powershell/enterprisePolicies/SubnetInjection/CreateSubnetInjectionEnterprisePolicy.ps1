﻿# Load the environment script
. "$PSScriptRoot\..\Common\EnterprisePolicyOperations.ps1"
. "$PSScriptRoot\ValidateVnetLocationForEnterprisePolicy.ps1"


function CreateSubnetInjectionEnterprisePolicy
{
     param(
        [Parameter(
            Mandatory=$true,
            HelpMessage="The Policy subscription"
        )]
        [string]$subscriptionId,

        [Parameter(
            Mandatory=$true,
            HelpMessage="The Policy resource group"
        )]
        [string]$resourceGroup,

        [Parameter(
            Mandatory=$true,
            HelpMessage="The Policy name"
        )]
        [string]$enterprisePolicyName,

        [Parameter(
            Mandatory=$true,
            HelpMessage="The Policy location"
        )]
        [string]$enterprisePolicylocation,

        [Parameter(
            Mandatory=$true,
            HelpMessage="Primary virtual network Id"
        )]
        [string]$primaryVnetId,

        [Parameter(
            Mandatory=$true,
            HelpMessage="Primary subnet name"
        )]
        [string]$primarySubnetName,

        [Parameter(
            Mandatory=$true,
            HelpMessage="Secondary virtual network Id"
        )]
        [string]$secondaryVnetId,

        [Parameter(
            Mandatory=$true,
            HelpMessage="Secondary subnet name"
        )]
        [string]$secondarySubnetName  

    )

    Write-Host "Logging In..." -ForegroundColor Green
    $connect = AzureLogin
    if ($false -eq $connect)
    {
        Write-Host "Error Logging In..." -ForegroundColor Red
        return
    }

    Write-Host "Logged In..." -ForegroundColor Green
    Write-Host "Creating Enterprise policy..." -ForegroundColor Green

    $primaryVnet = ValidateAndGetVnet -vnetId $primaryVnetId -enterprisePolicylocation $enterprisePolicylocation
    if ($primaryVnet -eq $null)
    {
       Write-Host "Subnet Injection Enterprise policy not created" -ForegroundColor Red
       return
    }

    $secondaryVnet = ValidateAndGetVnet -vnetId $secondaryVnetId -enterprisePolicylocation $enterprisePolicylocation
    if ($secondaryVnet -eq $null)
    {
       Write-Host "Subnet Injection Enterprise policy not created" -ForegroundColor Red
       return
    }

    $body = GenerateEnterprisePolicyBody -policyType "vnet" -policyLocation $enterprisePolicyLocation -policyName $enterprisePolicyName -primaryVnetId $primaryVnetId -primarySubnetName $primarySubnetName -secondaryVnetId $secondaryVnetId -secondarySubnetName $secondarySubnetName

    $result = PutEnterprisePolicy $resourceGroup $body
    if ($result -eq $false)
    {
       Write-Host "Subnet Injection Enterprise policy not created" -ForegroundColor Red
       return
    }
    Write-Host "Subnet Injection Enterprise policy created" -ForegroundColor Green 

    $policyArmId = "/subscriptions/$subscriptionId/resourceGroups/$resourceGroup/providers/Microsoft.PowerPlatform/enterprisePolicies/$enterprisePolicyName"
    $policy = GetEnterprisePolicy $policyArmId
    $policyString = $policy | ConvertTo-Json -Depth 7
    Write-Host "Policy created"
    Write-Host $policyString

}
CreateSubnetInjectionEnterprisePolicy