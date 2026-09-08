Feature: Zendesk Ticket Creation
  As a support manager
  I want negative reviews to automatically create Zendesk tickets
  So that my team can respond promptly

Background:
  Given the repository contains an unprocessed negative Apple review
  And a mocked Zendesk client that returns a new ticket ID "12345"

Scenario: Create a ticket for a negative review
  When the ticket creation timer runs
  Then a Zendesk ticket is created with the review's comment and rating
  And the review's ZendeskTicketId is updated to "12345"
  And the review's ProcessedAt timestamp is set

Scenario: Only unprocessed negative reviews are considered
  Given the repository also contains a negative review that already has a ZendeskTicketId
  When the ticket creation timer runs
  Then only the unprocessed review creates a ticket
  And the already-processed review is ignored

Scenario: Zendesk API failure does not stop other reviews
  Given the first negative review causes Zendesk.CreateTicketAsync to throw
  And there is a second negative review
  When the ticket creation timer runs
  Then a ticket is created for the second review
  And the first review's ZendeskTicketId remains null
  And an error is logged for the first review