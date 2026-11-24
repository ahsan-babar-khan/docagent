variable "subscription_id" {
  type        = string
  description = "Azure subscription id. Set with: export TF_VAR_subscription_id=$(az account show --query id -o tsv)"
}

variable "location" {
  type    = string
  default = "swedencentral"
}

variable "app_service_sku" {
  type    = string
  default = "F1"
}

variable "ai_endpoint" {
  type        = string
  default     = ""
  description = "OpenAI-compatible base URL reachable from Azure. Empty = api.openai.com. An Azure OpenAI URL (https://<res>.openai.azure.com/) is detected automatically. A local Ollama (localhost) is NOT reachable from App Service."
}

variable "ai_key" {
  type        = string
  sensitive   = true
  description = "API key for the provider. Pass with TF_VAR_ai_key, never in a file."
}

variable "chat_model" {
  type    = string
  default = "gpt-4o-mini"
}

variable "embedding_model" {
  type    = string
  default = "text-embedding-3-small"
}
