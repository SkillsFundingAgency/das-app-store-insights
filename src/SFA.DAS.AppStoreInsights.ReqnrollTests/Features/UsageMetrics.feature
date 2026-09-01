Feature: Usage Metrics
  As a product analyst
  I want daily download and install metrics from both stores
  So that I can track app performance

Background:
  Given the repository is ready to store usage metrics

Scenario: Fetch Apple metrics for yesterday
  Given the Apple client returns a metric with 100 downloads, 80 active users for yesterday
  When the Apple usage metrics timer runs
  Then a UsageMetric record is inserted with VendorId = 1
  And the Downloads = 100, ActiveUsers = 80

Scenario: Fetch Google metrics for yesterday
  Given the Google client returns a metric for yesterday with 200 downloads, and 150 active users
  When the Google usage metrics timer runs
  Then a UsageMetric record is inserted with VendorId = 2
  And the Downloads = 200, ActiveUsers = 150

Scenario: Handle missing metrics gracefully
  Given the Apple client returns an empty list
  When the Apple usage metrics timer runs
  Then no UsageMetric records are inserted
  And a log message indicates zero records inserted