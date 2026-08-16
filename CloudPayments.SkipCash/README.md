# CloudPayments.SkipCash

SkipCash adapter for AngryMonkey.CloudPayments. It supports Qatar-focused hosted payment creation, payment lookup, HMAC request signing, and signed webhooks.

Use AddSkipCashCloudPayments with credentials from the SkipCash merchant portal. The default URL targets the sandbox. Create requests require customer and address metadata: first_name, last_name, phone, email, street, city, state, country, and postal_code. SkipCash processes QAR.

Keep KeySecret and WebhookKey in a secret store. Review the official SkipCash integration manual and merchant sandbox before going live.
## Official sandbox resources

- [SkipCash integration manual](https://skipcash.app/assets/doc/SkipCashIntegrationManual.pdf)
- [SkipCash merchant portal](https://merchantportal.skipcash.app/)
- [SkipCash solutions](https://skipcash.app/solutions)