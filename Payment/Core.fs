// SPDX-FileCopyrightText: 2025 - HackLoad 2025 team
//
// SPDX-License-Identifier: MIT
module Payment.Core
open System
open System.Text.RegularExpressions

[<Literal>]
let CardAcceptTimeout = 15_000L * 60L

type MonetaryAmount = int64
type SecurityCode = int

[<CLIMutable>]
type PaymentIntent = {
    amount: MonetaryAmount
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

/// Status payment in the payment system.
type PaymentStatus = 
    /// The payment is created and waiting for further actions.
    | Created
    /// The payment is cancelled by the system.
    | Cancelled
    /// The payment is waiting for 3DS authorization.
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
    bankTransaction: string option
}
type BankErrorCode = 
    | AuthorizedSuccessfully
    | AuthorizationRequired
    | PotentialFraud
    | InvalidCardInformation
    | AuthorizationRejected

type BankInterface = 
    abstract member requestPayment: CardInformation -> MonetaryAmount -> (BankErrorCode * string)
    abstract member authorize: string -> SecurityCode -> (BankErrorCode * string)

type BankCardSecurity =
| NoSecurity
| ThreeDSecurity

type BankCardInformation = {
    card: CardInformation
    security: BankCardSecurity
}

type AcceptCardResult =
| PaymentSuccessul
| PaymentAuthorizationRequired
    
let mutable cards = [| 
    { card = { card_number = "1234567812345678"; card_holder_name = "ANDRII TESTER"; card_cvc = "123"; card_expiry = "01/28" }; security = NoSecurity }
|]

let createBank () =
    { new BankInterface 
        with member this.requestPayment (card: CardInformation) (amount: MonetaryAmount): BankErrorCode * string = 
                    let cardRecord = 
                        cards 
                        |> Array.tryFind (fun c -> c.card = card)
                    match cardRecord with
                    | None -> 
                        (InvalidCardInformation, "")
                    | Some cardRecord ->
                        match cardRecord.security with
                        | NoSecurity -> (AuthorizedSuccessfully, "123")
                        | ThreeDSecurity -> (AuthorizationRequired, "123")
             member this.authorize (transactionId: string) (securityCode: SecurityCode): BankErrorCode * string = 
                    if transactionId = "123" && securityCode = 344 then
                        (AuthorizedSuccessfully, transactionId) 
                    else
                        (AuthorizationRejected, transactionId) 
    }

type TransactionStorage() =
    let mutable storage : Transaction array = [| |]

    member this.clear() =
        storage <- [| |]
    
    member ths.findIntent merchant_id intent =
        storage |> Array.tryFind (fun t -> t.merchant_id = merchant_id && t.intent = intent)

    member this.createTransacton merchant_id date intent =
        let transaction_id = System.Guid.NewGuid().ToString()
        let new_transaction = { 
            transaction_id = transaction_id; merchant_id = merchant_id; 
            intent = intent; date = date; status = Created; card = None;
            bankTransaction = None
        }
        storage <- Array.insertAt storage.Length new_transaction  storage
        transaction_id
    
    member ths.findTransaction transaction_id =
        storage |> Array.tryFind (fun t -> t.transaction_id = transaction_id)
    
    member ths.setCard transaction_id cardInformation bankTransaction =
        let transactionIndex =
            storage 
            |> Array.findIndex (fun t -> t.transaction_id = transaction_id)
        storage[transactionIndex] <- { storage[transactionIndex] with status = Processing; card = Some cardInformation; bankTransaction = Some bankTransaction }
    
    member ths.set3DSCard transaction_id cardInformation bankTransaction =
        let transactionIndex =
            storage 
            |> Array.findIndex (fun t -> t.transaction_id = transaction_id)
        storage[transactionIndex] <- { storage[transactionIndex] with status = NeedApproval; card = Some cardInformation; bankTransaction = Some bankTransaction }
    
    member ths.setProcessing transaction_id =
        let transactionIndex =
            storage 
            |> Array.findIndex (fun t -> t.transaction_id = transaction_id)
        storage[transactionIndex] <- { storage[transactionIndex] with status = Processing }

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
    | BankRejected of BankErrorCode

let validCard (card: CardInformation) =
    Regex("\d{3}").IsMatch(card.card_cvc) 
        && Regex("\d{2}/\d{2}").IsMatch(card.card_expiry)
        && Regex("\d{12}").IsMatch(card.card_number)
        && not (String.IsNullOrWhiteSpace card.card_holder_name)

let bank = createBank()
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

let accept_card (transaction_id) (date: int64) (card: CardInformation) : Result<AcceptCardResult, ErrorCode> = 
    if validCard card then
        let transaction = storage.findTransaction transaction_id
        match transaction with
        | Some transaction ->
            if transaction.card.IsSome then
                if transaction.card.Value = card then
                    Ok PaymentSuccessul
                else
                    Error CardAlreadyAccepted
            elif transaction.date + CardAcceptTimeout <= date then
                Error TransactionNoLongerAvailable
            else
                let (bankResult, bank_transaction) = bank.requestPayment card transaction.intent.amount
                match bankResult with
                | AuthorizedSuccessfully -> 
                    storage.setCard transaction_id card bank_transaction
                    Ok PaymentSuccessul
                | AuthorizationRequired -> 
                    storage.set3DSCard transaction_id card bank_transaction
                    Ok PaymentAuthorizationRequired
                | BankErrorCode.InvalidCardInformation ->                     
                    Error InvalidCardInformation
                | PotentialFraud | AuthorizationRejected -> Error (BankRejected bankResult)
        | None -> 
            Error TransactionNotFound
    else
        Error InvalidCardInformation

let accept_3ds_authorization (transaction_id: string) (date: int64) (security_code: SecurityCode) : Result<string, ErrorCode> = 
    let transaction = storage.findTransaction transaction_id
    match transaction with
    | Some t when t.card.IsSome && t.bankTransaction.IsSome ->
        let (bankResult, bank_transaction) = bank.authorize t.bankTransaction.Value security_code
        match bankResult with
        | AuthorizedSuccessfully -> 
            storage.setProcessing transaction_id
            Ok bank_transaction
        | AuthorizationRejected -> 
            Error (BankRejected bankResult)
        | _ -> Error InvalidCardInformation
    | Some _ -> Error TransactionNotFound
    | None -> Error TransactionNotFound

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