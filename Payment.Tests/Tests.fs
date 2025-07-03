// SPDX-FileCopyrightText: 2025 - HackLoad 2025 team
//
// SPDX-License-Identifier: MIT
module Tests

open Xunit
open Payment.Core

open FsCheck.FSharp
open FsCheck.Xunit

open Generators

[<Properties( Arbitrary=[| typeof<ValidMerchant>; typeof<ValidPaymentIntent>; typeof<ValidCardInformation>; 
    typeof<Without3DSecureCardInformation>; typeof<With3DSecureCardInformation>; typeof<TimePeriodAfterTimeout> |] )>]
module AcceptinRequestsProperties =
    
    merchants <- defaultTestMerchants
    cards <- defaultBankAccounts
    storage.clear()

    [<Property>]
    let acceptingPaymentIntentProduceSameResults (merchant_id: string) (intent: PaymentIntent) (qty: uint8) =
        int qty <> 0 ==>
            let transaction_id = accept_payment_intent merchant_id (getDate()) intent
            let produced_same_transactions =
                Seq.init (int qty) (fun _ -> accept_payment_intent merchant_id (getDate()) intent)
                |> Seq.forall (fun transaction_id2 -> transaction_id = transaction_id2)
            produced_same_transactions

    [<Property>]
    let afterAcceptTransactionInStatusCreate (merchant_id: ValidMerchant) (intent: ValidPaymentIntent)  = 
        let date = getDate()
        let transaction_id = accept_payment_intent merchant_id.Get date intent.Get
        let transaction = transaction_id |> Result.bind getTransaction
        match transaction with
        | Ok t -> 
            t.merchant_id = merchant_id.Get && t.intent = intent.Get && t.status = Created
        | Error _ -> false

    [<Property>]
    let invalidMerchantProduceError (merchant_id: InvalidMerchant) (intent: PaymentIntent) =
        let transaction_id = accept_payment_intent merchant_id.Get (getDate()) intent
        match transaction_id with
        | Ok _ -> false
        | Error error_code ->
            match error_code with
            | MerchantNotFound -> true
            | _ -> false

    [<Property>]
    let differentMerchantsProduceDifferentTransactions (merchant_id1: ValidMerchant) (merchant_id2: ValidMerchant) (intent: ValidPaymentIntent) =
        merchant_id1 <> merchant_id2 ==>
            let transaction_id1 = accept_payment_intent merchant_id1.Get (getDate()) intent.Get
            let transaction_id2 = accept_payment_intent merchant_id2.Get (getDate()) intent.Get
            transaction_id1 <> transaction_id2

    [<Property(MaxRejected = 10_000)>]
    let cannotHavePaymentIntentWithEmptyAmount (merchant_id: ValidMerchant) (intent: PaymentIntent) =
        intent.amount = 0 ==>
            let transaction_id = accept_payment_intent merchant_id.Get (getDate()) intent
            match transaction_id with
            | Ok _ -> false
            | Error error_code -> 
                match error_code with
                | PaymentCreationError -> true
                | _ -> false

    [<Property>]
    let acceptCreditCard (merchant_id: ValidMerchant) (intent: ValidPaymentIntent) (card: ValidCardInformation) (time_after: ValidTimePeriod) =
        let transaction_id = accept_payment_intent merchant_id.Get (getDate()) intent.Get
        match transaction_id with
        | Ok transaction_id ->
            let result = accept_card transaction_id (getDate() + int64 time_after.Get) card.Get
            match result with
            | Ok _ -> 
                let t = getTransaction transaction_id |> Result.map (fun t -> t.status) |> Result.defaultValue Created
                // We expect the transaction to be in Processing or NeedApproval state
                t = Processing || t = NeedApproval
            | Error _ -> false
        | Error _ -> false

    [<Property>]
    let canAcceptSameCreditCardTwice (merchant_id: ValidMerchant) (intent: ValidPaymentIntent) (card: ValidCardInformation) =
        let transaction_id = accept_payment_intent merchant_id.Get (getDate()) intent.Get
        match transaction_id with
        | Ok transaction_id ->
            let result = accept_card transaction_id (getDate()) card.Get
            match result with
            | Ok _ -> 
                let result = accept_card transaction_id (getDate()) card.Get
                match result with
                | Ok _  -> 
                    let t = getTransaction transaction_id |> Result.map (fun t -> t.status) |> Result.defaultValue Created
                    // We expect the transaction to be in Processing or NeedApproval state
                    t = Processing || t = NeedApproval
                | _ -> false
            | Error _ -> false
        | Error _ -> false

    [<Property>]
    let cannotAcceptDifferentCreditCard (merchant_id: ValidMerchant) (intent: ValidPaymentIntent) (card: ValidCardInformation) (card2: ValidCardInformation) =
        let transaction_id = accept_payment_intent merchant_id.Get (getDate()) intent.Get
        match transaction_id with
        | Ok transaction_id ->
            let result = accept_card transaction_id (getDate()) card.Get
            match result with
            | Ok _ -> 
                let result = accept_card transaction_id (getDate()) card2.Get
                match result with
                | Error CardAlreadyAccepted -> true
                | _ -> false
            | Error _ -> false
        | Error _ -> false

    [<Property>]
    let invalidCardInformationNotAccepted (merchant_id: ValidMerchant) (intent: ValidPaymentIntent) (card: CardInformation) =
        // Precondition: card is invalid
        not (validCard card) ==>
            // Scenario: accept payment intent and then try to accept card
            let transaction_id = accept_payment_intent merchant_id.Get (getDate()) intent.Get
            match transaction_id with
            | Ok transaction_id ->
                let result = accept_card transaction_id (getDate()) card
                match result with
                | Error InvalidCardInformation -> true
                | _ -> false
            | Error _ -> false

    [<Property>]
    let cannnotAcceptCreditCardAfter15Min (merchant_id: ValidMerchant) (intent: ValidPaymentIntent) (card: ValidCardInformation) (time_after: uint32) =
        let tran_date = getDate()
        let transaction_id = accept_payment_intent merchant_id.Get tran_date intent.Get
        match transaction_id with
        | Ok transaction_id ->
            let result = accept_card transaction_id (tran_date + CardAcceptTimeout + int64 time_after) card.Get
            match result with
            | Error TransactionNoLongerAvailable -> true
            | _ -> false
        | Error _ -> false

    [<Property>]
    let acceptCreditCardWith3DSecureProduceAuthorizationRequest (merchant_id: ValidMerchant) (intent: ValidPaymentIntent) (card: With3DSecureCardInformation) (time_after: ValidTimePeriod) =
        let transaction_id = accept_payment_intent merchant_id.Get (getDate()) intent.Get
        match transaction_id with
        | Ok transaction_id ->
            let result = accept_card transaction_id (getDate() + int64 time_after.Get) card.GetCard
            match result with
            | Ok PaymentAuthorizationRequired -> 
                let t = getTransaction transaction_id |> Result.map (fun t -> t.status) |> Result.defaultValue Created
                // We expect the transaction to be in NeedApproval state
                t = NeedApproval
            | Ok _ -> 
                   false
            | _ -> false
        | Error _ -> false

    [<Property>]
    let acceptCreditCardWith3DSecureAndValid3DsProduceSuccess (merchant_id: ValidMerchant) (intent: ValidPaymentIntent) (card: With3DSecureCardInformation) (time_after: ValidTimePeriod) =
        let transaction_id = accept_payment_intent merchant_id.Get (getDate()) intent.Get
        match transaction_id with
        | Ok transaction_id ->
            let result = accept_card transaction_id (getDate() + int64 time_after.Get) card.GetCard
            match result with
            | Ok PaymentAuthorizationRequired -> 
                let result = accept_3ds_authorization transaction_id (getDate() + int64 time_after.Get) card.GetCode
                match result with
                | Ok _ -> 
                    let t = getTransaction transaction_id |> Result.map (fun t -> t.status) |> Result.defaultValue Created
                    // We expect the transaction to be in NeedApproval state
                    t = Processing
                | _ -> false
            | Ok _ -> 
                   false
            | _ -> false
        | Error _ -> false

[<Fact>]
let ``Regresson 1`` () =
    merchants <- defaultTestMerchants
    cards <- defaultBankAccounts
    storage.clear()
    let date = getDate()
    let intent =
        { amount = 1110L;
            currency = "";
            order_id = "";
            description = "";
            payment_type = "";
            user_id = "";
            email = "";
            phone = "";
            success_url = "";
            failure_url = "";
            callback_url = "";
            payment_lifetime = 0L;
            lang = "";
            metadata = Map.empty }
    let transaction_id = accept_payment_intent "hxeqnd852" date intent
    let transaction = transaction_id |> Result.bind getTransaction
    let result =
        match transaction with
        | Ok t ->
            t.date = date && t.merchant_id = "hxeqnd852" && t.intent = intent && t.status = Created
        | Error _ -> false
    Assert.True(result)

[<Fact>]
let ``Regresson 2`` () =
    merchants <- defaultTestMerchants
    cards <- defaultBankAccounts
    storage.clear()
    let date = getDate()
    let intent =
        { amount = 1110L;
            currency = "";
            order_id = "";
            description = "";
            payment_type = "";
            user_id = "";
            email = "";
            phone = "";
            success_url = "";
            failure_url = "";
            callback_url = "";
            payment_lifetime = 0L;
            lang = "";
            metadata = Map.empty }
    let transaction_id = accept_payment_intent "hxeqnd852" date intent
    let transaction = transaction_id |> Result.bind getTransaction
    let card : CardInformation = 
        noSecurityCards[0]
    let result =
        match transaction_id with
        | Ok transaction_id ->
            let result = accept_card transaction_id (getDate() + int64 0) card
            match result with
            | Ok _ -> true
            | Error _ -> false
        | Error _ -> false
    Assert.True(result)

[<Fact>]
let ``Regresson 3`` () =
    merchants <- defaultTestMerchants
    cards <- defaultBankAccounts
    storage.clear()
    let date = getDate()
    let intent =
        { amount = 1110L;
            currency = "";
            order_id = "";
            description = "";
            payment_type = "";
            user_id = "";
            email = "";
            phone = "";
            success_url = "";
            failure_url = "";
            callback_url = "";
            payment_lifetime = 0L;
            lang = "";
            metadata = Map.empty }
    let transaction_id = accept_payment_intent "hxeqnd852" date intent
    let transaction = transaction_id |> Result.bind getTransaction
    let card : CardInformation = 
        cardsWith3DSecure[0]
    let result =
        match transaction_id with
        | Ok transaction_id ->
            let result = accept_card transaction_id (getDate() + int64 0) card
            match result with
            | Ok PaymentAuthorizationRequired -> true
            | _ -> false
        | Error _ -> false
    Assert.True(result)
