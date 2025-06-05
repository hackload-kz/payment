module Payment
open System
open System.Text.RegularExpressions

[<Literal>]
let CardAcceptTimeout = 15_000L * 60L

[<CLIMutable>]
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
    card: CardInformation option
}

type TransactionStorage() =
    let mutable storage : Transaction array = [| |]
    
    member ths.findIntent merchant_id intent =
        storage |> Array.tryFind (fun t -> t.merchant_id = merchant_id && t.intent = intent)

    member this.createTransacton merchant_id date intent =
        let transaction_id = System.Guid.NewGuid().ToString()
        let new_transaction = { 
            transaction_id = transaction_id; merchant_id = merchant_id; 
            intent = intent; date = date; status = Created; card = None
        }
        storage <- Array.insertAt storage.Length new_transaction  storage
        transaction_id
    
    member ths.findTransaction transaction_id =
        storage |> Array.tryFind (fun t -> t.transaction_id = transaction_id)
    
    member ths.setCard transaction_id cardInformation =
        let transactionIndex =
            storage 
            |> Array.findIndex (fun t -> t.transaction_id = transaction_id)
        storage[transactionIndex] <- { storage[transactionIndex] with status = Processing; card = Some cardInformation }

type ErrorCode =
    | InvalidCardNumber
    | InvalidCardExpiry
    | InvalidCardCVC
    | InvalidCardHolderName
    | TransactionNotFound
    | MerchantNotFound
    | PaymentCreationError
    | CardAlreadyAccepted
    | TransactionNoLongerAvailable
    | InvalidCardInformation

let validCard (card: CardInformation) =
    Regex("\d{3}").IsMatch(card.card_cvc) 
        && Regex("\d{2}/\d{2}").IsMatch(card.card_expiry)
        && Regex("\d{12}").IsMatch(card.card_number)
        && not (String.IsNullOrWhiteSpace card.card_holder_name)

let storage = TransactionStorage()
//let mutable storage : Transaction array = [| |]
let mutable merchants : Merchant array = [| |]
let merchantExists m = 
    merchants |> Array.exists (fun em -> m = em.merchant_id)

let validate_merchant m p =
    merchants |> Array.exists (fun em -> m = em.merchant_id && p = em.merchant_key)
    
let accept_payment_intent (merchant_id: string) (date: int64) (intent: PaymentIntent) : Result<string, ErrorCode> = 
    if not (merchantExists merchant_id) then 
        Error MerchantNotFound
    elif intent.amount <= 0 then Error PaymentCreationError
    else
        let existing_transaction = 
            storage.findIntent merchant_id intent
        match existing_transaction with
        | Some transaction -> 
            Ok transaction.transaction_id
        | None -> 
            let transaction_id = storage.createTransacton merchant_id date intent
            Ok transaction_id

let accept_card (transaction_id) (date: int64) (card: CardInformation) : Result<unit, ErrorCode> = 
    if validCard card then
        let transaction = storage.findTransaction transaction_id
        match transaction with
        | Some transaction ->
            if transaction.card.IsSome then
                Error CardAlreadyAccepted
            elif transaction.date + CardAcceptTimeout <= date then
                Error TransactionNoLongerAvailable
            else
                storage.setCard transaction_id card
                Ok ()
        | None -> 
            Error TransactionNotFound
    else
        Error InvalidCardInformation

let confirm_transaction (transaction_id: string) = 
    ()

let notify_transaction_status (transaction_id: string) (status: string) = 
    ()

let withdraw (card_number: string) (amount: int64) = 
    ()

let getTransaction (transaction_id: string) = 
    let transaction = storage.findTransaction transaction_id
    match transaction with
    | Some t -> Ok t
    | None -> Error TransactionNotFound

/// Returns the current date in milliseconds since Unix epoch.
let getDate () =
    let date = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    date