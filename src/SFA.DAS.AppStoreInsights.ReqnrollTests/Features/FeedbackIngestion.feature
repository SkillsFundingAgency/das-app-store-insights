Feature: Feedback Ingestion
  As an operations team
  I want to fetch new reviews from Apple and Google daily
  So that I can monitor user sentiment and create tickets for negative feedback

Background:
  Given the system is configured with valid Apple and Google credentials
  And the in-memory repository is empty

Scenario: Fetch new Apple reviews
  Given the Apple API returns 2 new reviews (1 positive, 1 negative)
  When the feedback ingestion timer runs
  Then exactly 2 Apple reviews are inserted into the repository
  And the negative review is marked as IsNegative = true

Scenario: Fetch new Google reviews
  Given the Google API returns 1 new review with rating 1
  When the feedback ingestion timer runs
  Then exactly 1 Google review is inserted into the repository
  And that review has IsNegative = true

Scenario: Duplicate reviews are ignored
  Given an Apple review with external ID "abc123" already exists
  And the Apple API returns the same review "abc123" again
  When the feedback ingestion timer runs
  Then no new reviews are inserted

Scenario: API failure is logged and exception is thrown
  Given the Apple API throws an exception
  When the feedback ingestion timer runs
  Then an exception is propagated out of the function
  And the error is logged