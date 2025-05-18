open Payment

let tran_id = accept_payment_intent "merchant_id"  (getDate()) {
    amount = 100_000L
    currency = "KZT"
    order_id = "order_id"
    description = "description"
    payment_type = "payment_type"
    user_id = "user_id"
    email = "email"
    phone = "phone"
    success_url = "success_url"
    failure_url = "failure_url"
    callback_url = "callback_url"
    payment_lifetime = 1000L
    lang = "lang"
    metadata = Map.empty
}
match tran_id with
| Ok tran_id ->
    accept_card tran_id (getDate()) {
        card_number = "1234567890123456"
        card_expiry = "12/23"
        card_cvc = "123"
        card_holder_name = "John Doe"
    } |> ignore
    confirm_transaction tran_id |> ignore
| Error e ->
    printf "%A" e
// For more information see https://aka.ms/fsharp-console-apps
printfn "Hello from F#"
