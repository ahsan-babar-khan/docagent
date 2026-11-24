terraform {
  required_version = ">= 1.6"
  required_providers {
    azurerm = { source = "hashicorp/azurerm", version = "~> 4.0" }
    random  = { source = "hashicorp/random", version = "~> 3.6" }
  }
  # Local state is git-ignored. A team would use a remote backend, e.g.:
  # backend "azurerm" { resource_group_name = "rg-tfstate" storage_account_name = "..." container_name = "tfstate" key = "docagent.tfstate" }
}

provider "azurerm" {
  features {}
  subscription_id = var.subscription_id
}

resource "random_string" "suffix" {
  length  = 5
  upper   = false
  special = false
}

resource "azurerm_resource_group" "rg" {
  name     = "rg-docagent"
  location = var.location
}

resource "azurerm_log_analytics_workspace" "logs" {
  name                = "log-docagent-${random_string.suffix.result}"
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
}

resource "azurerm_application_insights" "ai" {
  name                = "appi-docagent-${random_string.suffix.result}"
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  workspace_id        = azurerm_log_analytics_workspace.logs.id
  application_type    = "web"
}

resource "azurerm_service_plan" "plan" {
  name                = "asp-docagent"
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  os_type             = "Linux"
  sku_name            = var.app_service_sku # F1 = free; B1 if you need always-on for demos
}

resource "azurerm_linux_web_app" "app" {
  name                = "docagent-${random_string.suffix.result}"
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  service_plan_id     = azurerm_service_plan.plan.id
  https_only          = true

  # Managed identity: if you later use Azure OpenAI, it lets you drop the API key (Step 11.2) by granting
  # "Cognitive Services OpenAI User" on the Azure OpenAI resource to this identity.
  identity {
    type = "SystemAssigned"
  }

  site_config {
    always_on = var.app_service_sku != "F1" # the free tier does not support always-on
    application_stack {
      dotnet_version = "8.0"
    }
  }

  app_settings = {
    "AI__Endpoint"                          = var.ai_endpoint # empty = api.openai.com; Azure OpenAI URL is detected
    "AI__Key"                               = var.ai_key
    "AI__ChatModel"                         = var.chat_model
    "AI__EmbeddingModel"                    = var.embedding_model
    "ConnectionStrings__Db"                 = "Data Source=/home/data/docagent.db" # /home survives restarts
    "Search__Mode"                          = "lsh"
    "APPLICATIONINSIGHTS_CONNECTION_STRING" = azurerm_application_insights.ai.connection_string
  }
}
