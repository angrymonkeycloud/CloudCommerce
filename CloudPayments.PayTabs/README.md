# CloudPayments.PayTabs

PayTabs adapter for AngryMonkey.CloudPayments. It supports hosted sale and authorization pages, capture, void, full or partial refund, transaction query, and signed callbacks or IPNs.

Configure it with AddPayTabsCloudPayments. Choose the correct regional PayTabs base URL and keep the ServerKey in a secret store. Payment metadata may provide customer_name, customer_email, customer_phone, street, city, state, country, and postal_code.

Use a PayTabs test profile and the official test-card list before switching to a live profile.
## Official sandbox resources

- [PayTabs hosted payment API](https://support.paytabs.com/en/support/solutions/articles/60000992876-3-2-1-hosted-payment-page-apis-initiating-the-payment)
- [PayTabs test cards](https://support.paytabs.com/en/support/solutions/articles/60000712315-what-are-the-test-cards-available-to-perform-payments-)
- [PayTabs callback signature verification](https://support.paytabs.com/en/support/solutions/articles/60000718961)