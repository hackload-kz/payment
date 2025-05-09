module Payment
open System
type PaymentIntent = {
    amount: int64
    currency: string
    order_id: string
    description: string
    payment_type: string
    user_id: string
    email: string
    phone: string
    success_url: string
    failure_url: string
    callback_url: string
    payment_lifetime: int64
    lang: string
    metadata: Map<string, string>
}

type CardInformation = {
    card_number: string
    card_expiry: string
    card_cvc: string
    card_holder_name: string
}

type Merchant = {
    merchant_id: string
    merchant_key: string
}

type PaymentStatus = 
    | Created
    | Cancelled
    | NeedApproval
    | Hold
    | Clearing
    | Finished
    | Processing
    | Failed

type Transaction = {
    transaction_id: string
    merchant_id: string
    date: int64
    intent: PaymentIntent
    status: PaymentStatus
}

type ErrorCode =
    | InvalidCardNumber
    | InvalidCardExpiry
    | InvalidCardCVC
    | InvalidCardHolderName
    | TransactionNotFound
    | MerchantNotFound
    | PaymentCreationError

let mutable storage : Transaction array = [| |]
let mutable merchants : Merchant array = [| |]
let merchantExists m = 
    merchants |> Array.exists (fun em -> m = em.merchant_id)
    
let accept_payment_intent (merchant_id: string) (date: int64) (intent: PaymentIntent) : Result<string, ErrorCode> = 
    if not (merchantExists merchant_id) then 
        Error MerchantNotFound
    elif intent.amount <= 0 then Error PaymentCreationError
    else
        let existing_transaction = 
            storage |> Array.tryFind (fun t -> t.merchant_id = merchant_id && t.intent = intent)
        match existing_transaction with
        | Some transaction -> 
            Ok transaction.transaction_id
        | None -> 
            let transaction_id = System.Guid.NewGuid().ToString()
            let new_transaction = { 
                transaction_id = transaction_id; merchant_id = merchant_id; 
                intent = intent; date = date; status = Created;
            }
            storage <- Array.insertAt storage.Length new_transaction  storage
            Ok transaction_id

let accept_card (transaction_id) (card: CardInformation) = 
    ()

let confirm_transaction (transaction_id: string) = 
    ()

let notify_transaction_status (transaction_id: string) (status: string) = 
    ()

let withdraw (card_number: string) (amount: int64) = 
    ()

let getTransaction (transaction_id: string) = 
    let transaction = storage |> Array.tryFind (fun t -> t.transaction_id = transaction_id)
    match transaction with
    | Some t -> Ok t
    | None -> Error TransactionNotFound

let getDate () =
    let date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    date