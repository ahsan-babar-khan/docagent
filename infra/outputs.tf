output "app_name" {
  value = azurerm_linux_web_app.app.name
}

output "app_url" {
  value = "https://${azurerm_linux_web_app.app.default_hostname}"
}

output "app_principal_id" {
  description = "Managed identity of the web app (for the keyless Azure OpenAI role assignment)."
  value       = azurerm_linux_web_app.app.identity[0].principal_id
}
