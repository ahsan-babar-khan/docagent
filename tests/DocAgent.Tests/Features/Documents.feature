Feature: Upload and find documents by metadata
  As a project manager
  I want to store documents with business context
  So that I can find them by project, customer and type

  Scenario: Upload a document with metadata
    When I upload "Contract_Acme.txt" with project "Apollo", customer "Acme" and type "Contract"
    Then the response status is 201
    And the document is listed under project "Apollo"

  Scenario: Reject a document without a project
    When I upload "Notes.txt" with project "", customer "Acme" and type "Memo"
    Then the response status is 400
