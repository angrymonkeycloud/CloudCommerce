# CloudPayments.MyFatoorah

MyFatoorah adapter for AngryMonkey.CloudPayments. It supports hosted create and authorize flows, capture or release, refund requests, payment lookup, and signed webhook events without adding MyFatoorah concerns to the core package.

Use AddMyFatoorahCloudPayments and keep the API token and webhook secret in a secret store. The default API URL targets the MyFatoorah test environment. Payment requests may provide customer_name, customer_email, and customer_mobile metadata.

MyFatoorah refund calls create a provider-side refund request that may remain pending while it is reviewed. Consult the official MyFatoorah documentation for live country endpoints, sandbox tokens, and test cards.
## Official sandbox resources

- [Get started and create a MyFatoorah sandbox token](https://docs.myfatoorah.com/docs/get-started)
- [MyFatoorah test cards](https://docs.myfatoorah.com/docs/test-cards)
- [Webhook v2 and signature guidance](https://docs.myfatoorah.com/docs/webhook-v2)