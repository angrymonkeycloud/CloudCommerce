# CloudPayments.Tap

Tap Payments adapter for AngryMonkey.CloudPayments. It supports hosted charges, authorization, capture, void, full or partial refunds, retrieval, saved payment source references, and signed webhooks.

Configure it with AddTapCloudPayments and store the secret key securely. Payment metadata may include first_name, last_name, email, phone_country_code, phone_number, source_id, and statement_descriptor. A Tap customer reference can be passed as CustomerReference.

Use Tap test keys with the official test-card documentation before enabling live credentials.
## Official sandbox resources

- [Tap Payments get started](https://developers.tap.company/docs/get-started)
- [Tap testing cards](https://developers.tap.company/reference/testing-cards)
- [Tap webhook verification](https://developers.tap.company/docs/webhook)