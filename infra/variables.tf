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

variable "openai_endpoint" {
  type        = string
  description = "e.g. https://<your-resource>.openai.azure.com/"
}

variable "openai_key" {
  type        = string
  sensitive   = true
  description = "Pass with TF_VAR_openai_key, never in a file."
}

variable "chat_deployment" {
  type    = string
  default = "gpt-4o-mini"
}

variable "embedding_deployment" {
  type    = string
  default = "text-embedding-3-small"
}
