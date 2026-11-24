Feature: Ask questions about documents
  Answers must be grounded in the stored documents and cite them

  Background:
    Given a document "Contract_Acme.txt" in project "Apollo" containing "The Acme contract renews on 1 March 2027."

  Scenario: Answer cites its source
    When I ask "When does the Acme contract renew?"
    Then the answer cites "Contract_Acme.txt"

  Scenario: Refuse to guess when nothing matches
    When I ask "What is the capital of Australia?"
    Then the answer says it could not find this in the documents
