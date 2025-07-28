# I-Business Payment API Error Codes Specification

## Overview
Comprehensive error code reference for I-Business payment API operations. Error codes provide standardized identification of issues across all payment lifecycle states and API methods, enabling proper error handling, debugging, and user experience optimization.

## Error Code Structure
- **Code**: Numeric identifier (0 = success, non-zero = error)
- **Message**: Brief, user-friendly error description
- **Details**: Optional technical details for debugging
- **Context**: Applicable API methods and payment states

## Success Code
| Code | Message | Details | Description |
|------|---------|---------|-------------|
| 0 | None | - | Successful operation |

## System and Configuration Errors (1-99)

### Core System Errors
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 1 | Параметры не сопоставлены | - | Parameter mapping failed |
| 2 | Отсутствуют обязательные параметры | - | Required parameters missing |
| 3 | Внутренняя ошибка системы интернет-эквайринга | - | Internal acquiring system error |
| 4 | Не получится изменить статус платежа | - | Invalid state transition attempted |
| 5 | Обратитесь в поддержку, чтобы уточнить детали | - | Support contact required |

### Card and Customer Management Errors
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 6 | Не получилось привязать карту покупателя. Обратитесь в поддержку, чтобы уточнить детали | - | Card binding failed |
| 7 | Неверный статус покупателя | - | Invalid customer status |
| 8 | Неверный статус транзакции | - | Invalid transaction status |

### URL and Routing Errors  
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 9 | Переадресовываемый URL пуст | - | Redirect URL empty |
| 10 | Метод Charge заблокирован для данного терминала | - | Charge method blocked |
| 11 | Невозможно выполнить платеж | - | Payment execution impossible |
| 12 | Неверный параметр RedirectDueDate | - | Invalid redirect expiration |

### Payment Method Availability
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 13 | Оплата с мобильного телефона недоступна | - | Mobile payment unavailable |
| 13 | Оплата через WebMoney недоступна | - | WebMoney payment unavailable |
| 14 | Платеж неверный | - | Invalid payment |
| 15 | Не удалось осуществить платеж через EINV | - | EINV payment failed |
| 16 | Счет был отклонен | - | Invoice rejected |
| 17 | Неверные введенные данные | - | Invalid input data |
| 18 | Не удалось осуществить платеж через MC | - | MasterCard payment failed |
| 19 | Не удалось осуществить платеж через WebMoney | - | WebMoney payment failed |
| 20 | Ошибка повторного идентификатора заказа | - | Duplicate order ID error |
| 21 | Внутренняя ошибка вызова сервиса ACQAPI | - | ACQAPI service error |

### System Maintenance and Service Errors
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 27 | Кассовая ссылка на текущий момент недоступна для повторной активации | - | Cash register link reactivation unavailable |
| 50 | Ошибка отправки нотификации | - | Notification sending failed |
| 51 | Ошибка отправки email | - | Email sending failed |
| 52 | Ошибка отправки SMS | - | SMS sending failed |
| 53 | Обратитесь к продавцу | - | Contact merchant |
| 54 | Повторное прохождение 3DS авторизации не допустимо | - | Repeat 3DS not allowed |
| 55 | Повторите попытку позже | Не найдено оплаченных назначений платежа | No paid payment assignments found |

### Document and Receipt Service Errors
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 60 | Запрещено получение документов по URL для текущего терминала | Запрещено получение документов по URL для текущего терминала | Document URL access forbidden |
| 61 | Должен быть заполнен один из параметров: emailList или URL | Должен быть заполнен один из параметров: emailList или URL | Email or URL required |
| 62 | Запрещено получение документов по URL для текущего systemId | Запрещено получение документов по URL для текущего systemId | System ID document access forbidden |
| 63 | Не найдена операция | Не найдена операция | Operation not found |
| 64 | Невалидные данные в запросе | Невалидные данные в запросе | Invalid request data |
| 65 | Не удалось сформировать документ. Обратитесь в службу поддержки | Не удалось сформировать документ. Повторите операцию позднее | Document generation failed |
| 66 | Не удалось сформировать документ. Повторите операцию позднее | Запрещено получение документов по URL для текущего терминала | Document generation retry required |
| 67 | Не удалось сформировать документ. Повторите операцию позднее | Не удалось сформировать документ. Повторите операцию позднее | Document generation temporary failure |
| 68 | Не удалось сформировать документ. Обратитесь в службу поддержки | Стороний сервис не доступен | External service unavailable |

### Foreign Card Restrictions
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 76 | Операция по иностранной карте недоступна | Операция по иностранной карте недоступна. Воспользуйтесь картой российского банка | Foreign card operation unavailable |
| 77 | Оплата иностранной картой недоступна | Оплата по иностранной карте недоступна. Воспользуйтесь картой российского банка | Foreign card payment unavailable |
| 78 | Выплата на иностранную карту недоступна | Выплата на иностранную карту недоступна. Воспользуйтесь картой российского банка | Foreign card payout unavailable |
| 79 | Возврат на иностранную карту недоступен | Возврат на иностранную карту недоступен. Обратитесь в поддержку | Foreign card refund unavailable |

### Service Integration Errors
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 96 | Ошибка Iris | - | Iris service error |
| 97 | Ошибка Jasper | - | Jasper service error |
| 98 | Ошибка SubExt | - | SubExt service error |
| 99 | Попробуйте повторить попытку позже | Банк, выпустивший карту, отклонил операцию | Bank rejected operation |

## Payment Processing Errors (100-199)

### General Payment Processing
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 100 | Попробуйте еще раз. Если ошибка повторится — обратитесь в поддержку | - | Multiple scenarios (see variations) |
| 101 | Не пройдена идентификация 3DS | Ошибка прохождения 3-D Secure | 3DS authentication failed |
| 102 | Обратитесь в поддержку, чтобы уточнить детали | - | Multiple scenarios (see variations) |
| 103 | Недостаточно средств на счете | - | Insufficient account funds |
| 104 | Ошибка выполения рекуррента | - | Recurring payment error |
| 105 | Нужно настроить автоплатежи по Maestro — для этого обратитесь в поддержку | - | Maestro autopayment setup required |

### Card and 3DS Errors
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 106 | Карта не поддерживает 3DS проверку. Попробуйте другую карту | - | Card doesn't support 3DS |
| 107 | Неверно введен CardId. Проверьте, что такая карта была ранее привязана | - | Invalid CardId |
| 108 | Оплата разрешена только по 3DS картам. Попробуйте другую карту | - | 3DS-only payment required |
| 109 | Не найден dsTranId для сессии | - | 3DS transaction ID not found |
| 110 | Не передан cres | - | 3DS challenge response missing |
| 111 | Передан некорректный cres | - | Invalid 3DS challenge response |

### Insufficient Funds and Limits
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 116 | Недостаточно средств на карте | - | Insufficient card funds |
| 119 | Превышено допустимое количество попыток авторизации операции | - | Authorization attempt limit exceeded |
| 120 | Попробуйте повторить попытку позже | - | Temporary processing issue |
| 123 | Попробуйте повторить попытку позже | - | Temporary processing issue |
| 125 | Попробуйте повторить попытку позже | - | Temporary processing issue |
| 191 | Некорректный статус договора, обратитесь к вашему менеджеру | - | Invalid contract status |

## Validation Errors (200-399)

### Required Field Validation
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 201 | Поле PaymentId не должно быть пустым | - | PaymentId required |
| 201 | Поле paymentMethod не должно быть пустым | - | Payment method required |
| 201 | Поле paymentObject не должно быть пустым | - | Payment object required |
| 201 | Поле measurementUnit не должно быть пустым | - | Measurement unit required |
| 202 | Терминал заблокирован | - | Terminal blocked |
| 203 | Параметры запроса не должны быть пустыми | - | Request parameters required |

### Authentication Errors
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 204 | Неверный токен. Проверьте пару TerminalKey/SecretKey | - | Invalid token |
| 205 | Неверный токен. Проверьте пару TerminalKey/SecretKey | Указанный терминал не найден | Terminal not found |
| 206 | email не может быть пустым | - | Email required |

### Data Size Validation
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 207 | Параметр DATA превышает максимально допустимый размер | - | DATA parameter too large |
| 208 | Наименование ключа из параметра DATA превышает максимально допустимый размер | - | DATA key name too long |
| 209 | Значение ключа из параметра DATA превышает максимально допустимый размер | - | DATA key value too long |

### Field Size Validation (210-243)
| Code | Message Template | Context |
|------|------------------|---------|
| 210 | Размер поля TerminalKey должен быть от {min} до {max} | Terminal key size validation |
| 211 | Неверный формат IP | IP format validation |
| 212 | Размер поля OrderId должен быть от {min} до {max} | Order ID size validation |
| 213 | Размер поля Description должен быть от {min} до {max} | Description size validation |
| 214 | Поле Currency должно быть меньше или равно {value} | Currency validation |
| 215 | Размер поля PayForm должен быть от {min} до {max} | Payment form size validation |
| 216 | Размер поля CustomerKey должен быть от {min} до {max} | Customer key size validation |

### Numeric and Format Validation (217-262)
| Code | Message | Context |
|------|---------|---------|
| 217 | Поле PaymentId числовое значение должно укладываться в формат | Payment ID format |
| 218 | Значение PAN не является числовым | Card PAN validation |
| 219 | Неверный срок действия карты | Card expiry validation |
| 220 | Размер поля CardHolder должен быть от {min} до {max} | Card holder validation |
| 221 | Значение CVV не является числовым | CVV validation |
| 224 | Неверный формат email | Email format validation |
| 251 | Неверная сумма. Сумма должна быть больше или равна {value} копеек | Amount validation |
| 252 | Срок действия карты истек | Card expiry validation |
| 253 | Валюта {value} не разрешена для данного терминала | Currency restriction |

## Receipt and Fiscal Errors (300-399)

### Receipt Validation
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 308 | Сумма всех позиций в чеке должна равняться сумме всех видов оплаты | - | Receipt total mismatch |
| 309 | Поле Receipt не должно быть пустым | - | Receipt required |
| 310 | Дробная часть параметра Quantity не должна быть больше {value} знаков | - | Quantity precision validation |
| 311 | Ошибка регистрации чека в Receipt Service | - | Receipt service registration error |
| 312 | Ошибка получения чека из Receipt Service | - | Receipt service retrieval error |
| 313 | Ошибка создания организации в Receipt Service | - | Organization creation error |
| 314 | Ошибка создания кассы в Receipt Service | - | Cash register creation error |
| 315 | Касса не найдена | - | Cash register not found |

### Agent and Supplier Information
| Code | Message | Context |
|------|---------|---------|
| 317 | Неверное значение поля agentSign | Agent sign validation |
| 318 | Поле AgentSign не должно быть пустым | Agent sign required |
| 319 | Поле SupplierInfo не должно быть пустым | Supplier info required |
| 320 | Поле Inn в объекте SupplierInfo не должно быть пустым | Supplier INN required |

### Marketplace and Operations
| Code | Message | Context |
|------|---------|---------|
| 332 | Поле Fee в объекте Shops должно быть больше или равно 0 | Shop fee validation |
| 333 | Поле Amount в объекте Shops должно быть больше или равно 1 | Shop amount validation |
| 334 | Суммы в чеке и в платеже не совпадают | Receipt/payment amount mismatch |
| 335 | OrderId {value} не найден для TerminalKey {value} | Order not found |

### Marked Goods Validation (383-386)
| Code | Message | Context |
|------|---------|---------|
| 383 | Поле markProcessingMode должно быть заполнено для маркированных товаров | Mark processing mode required |
| 383 | Поле markCode должно быть заполнено для маркированных товаров | Mark code required |
| 383 | Поле markQuantity должно быть заполнено для маркированных товаров | Mark quantity required |
| 385 | Поле markQuantity передается только для маркированных товаров | Mark quantity for marked goods only |
| 386 | markQuantity заполняется только для дробного расчета за штучный маркированный товар | Mark quantity for fractional calculation |

## Service and Limit Errors (400-699)

### Internal Service Errors
| Code | Message | Context |
|------|---------|---------|
| 401 | Внутренняя ошибка системы | Internal system error |
| 402 | Повторите попытку позже | Retry later |
| 403 | Превышен лимит на количество пополнений в месяц | Monthly topup limit exceeded |
| 404 | Превышен лимит на сумму пополнения через бесконтактные сервисы | Contactless topup limit exceeded |
| 405 | Превышен лимит на сумму пополнения по виртуальной карте | Virtual card topup limit exceeded |
| 406 | Превышен лимит на сумму пополнения в месяц через мобильное приложение | Mobile app monthly limit exceeded |

### Certificate Management (411-417)
| Code | Message | Context |
|------|---------|---------|
| 411 | Сертификат не найден | Certificate not found |
| 412 | Истек срок действия сертификата | Certificate expired |
| 413 | Сертификат заблокирован | Certificate blocked |
| 414 | Сертификат уже сохранен для данного терминала | Certificate already saved |
| 415 | Дата начала срока действия сертификата еще не наступила | Certificate not yet valid |
| 417 | Ошибка обработки сертификата | Certificate processing error |

### Card Binding Errors (500-515)
| Code | Message | Context |
|------|---------|---------|
| 500 | Добавление карты к данному терминалу запрещено | Card binding forbidden |
| 501 | Терминал не найден | Terminal not found |
| 502 | Карта по requestKey не найдена | Card by request key not found |
| 503 | CustomerKey не найден | Customer key not found |
| 504 | Не удалось провести платеж при привязке карты | Payment during card binding failed |
| 505 | Не удалось привязать карту. Внутренняя ошибка | Card binding internal error |
| 506 | Карта добавлена в черный список | Card blacklisted |
| 507 | Карта не поддерживает 3DS проверку. Попробуйте другую карту | Card doesn't support 3DS |
| 508 | Неверный номер карты | Invalid card number |
| 510 | Карта уже привязана к переданному CustomerKey | Card already bound |
| 511 | Проверка 3DS не пройдена | 3DS verification failed |
| 514 | Введена неверная сумма холдирования | Invalid hold amount |

### Card and Limit Restrictions (600-699)
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 600 | Карта добавлена в черный список | - | Card blacklisted |
| 600 | Интернет-магазин отклонил операцию по данной карте | - | Merchant rejected card |
| 601 | Разрешены операции только по Master Card | - | MasterCard only |
| 603 | Превышено количество попыток оплаты с данной карты | - | Card attempt limit exceeded |
| 619 | Отсутствуют обязательные данные отправителя | Не переданы персональные данные отправителя для операции emoney2card больше 15000 руб. | Sender data required for large transfers |
| 620 | Проверьте сумму — она не может быть равна 0 | Сумма операции не может быть равна 0 | Amount cannot be zero |
| 632 | Превышен лимит на сумму операции | Лимит на сумму пополнения emoney2card | Operation amount limit exceeded |
| 642 | Проверьте номер карты | Карта не прошла проверку по алгоритму Луна | Card failed Luhn check |
| 648 | Магазин заблокирован или еще не активирован | - | Shop blocked or not activated |

## Bank Response Errors (1000-1999)

### Bank Communication Errors
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 1001 | Свяжитесь с банком | Свяжитесь с банком, выпустившим карту, чтобы провести платеж | Contact issuing bank |
| 1003 | Неверный магазин | Неверный номер магазина. Идентификатор магазина недействителен | Invalid merchant ID |
| 1004 | Банк, который выпустил карту, считает платеж подозрительным | - | Suspicious payment |
| 1005 | Платеж отклонен банком, выпустившим карту | Платеж отклонен банком, выпустившим карту | Bank rejected payment |

### Card Validation Errors
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 1014 | Карта недействительна | Неправильные реквизиты — проверьте их или воспользуйтесь другой картой | Invalid card |
| 1015 | Неверный номер карты | Неверный номер карты | Invalid card number |
| 1033 | Истек срок действия карты | - | Card expired |
| 1054 | Истек срок действия карты | Неправильные реквизиты — проверьте их или воспользуйтесь другой картой | Card expired |
| 1080 | Плательщик ввел неверный срок действия карты | - | Incorrect expiry date entered |
| 1082 | Неверный CVV | Неправильные реквизиты — проверьте их или воспользуйтесь другой картой | Invalid CVV |

### Insufficient Funds and Limits
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 1013 | Банк, который выпустил карту, отклонил платеж — сумма превышает лимит по карте | Сумма превышает лимит платежа вашего банка. Воспользуйтесь другой картой или обратитесь в банк | Amount exceeds card limit |
| 1051 | Недостаточно средств на карте | Не получилось оплатить. На карте недостаточно средств | Insufficient card funds |
| 1061 | Покупатель превысил лимит платежей по своей карте | - | Customer payment limit exceeded |
| 1065 | Покупатель превысил лимит платежей по своей карте | - | Customer payment limit exceeded |
| 1075 | Покупатель оплатил максимум раз по своей карте за день | - | Daily payment limit exceeded |

### Card Security and Fraud
| Code | Message | Context |
|------|---------|---------|
| 1041 | Карта утеряна | Card reported lost |
| 1057 | Покупатель запретил такие операции для своей карты | Customer restricted operations |
| 1058 | Покупатель запретил такие операции для своей карте | Customer restricted operations |
| 1059 | Банк, который выпустил карту, считает платеж подозрительным | Suspicious payment |
| 1063 | Банк, который выпустил карту, считает платеж подозрительным | Suspicious payment |

### System and Processing Errors
| Code | Message | Context |
|------|---------|---------|
| 1018 | Неизвестный статус платежа | Unknown payment status |
| 1071 | Токен просрочен | Token expired |
| 1085 | Операция успешна | Operation successful |
| 1096 | Системная ошибка | System error |
| 1099 | Способ оплаты отключен | Payment method disabled |

### Merchant Limit Errors (1200-1299)
| Code | Message | Context |
|------|---------|---------|
| 1202 | Сумма платежа превышает лимит по разовой операции в этом магазине | Single operation limit exceeded |
| 1203 | Сумма платежа превышает лимит по разовой операции или количеству операций в этом магазине | Operation limits exceeded |
| 1204 | Достигнут лимит по суточному обороту | Daily turnover limit reached |
| 1205 | Магазин не принимает карты этой страны | Country card restriction |
| 1235 | Для карт «Мир» нужно настроить подтверждение платежей по СМС 3DS 2.0 | Mir card 3DS 2.0 required |

## SBP (Faster Payment System) Errors (3000-5999)

### SBP Configuration Errors
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 3001 | Оплата через QrPay недоступна | Ошибка возникает, если для терминала не активирован способ оплаты СБП | SBP not activated |
| 3002 | Недостаточный баланс счёта для отмены | Ошибка возникает, если на расчетном счете магазина недостаточно средств для возврата | Insufficient merchant balance |
| 3016 | Невозможно создать QR | Ошибка передается, если сумма операции по СБП не соответствует диапазону возможной суммы | QR creation failed - amount out of range |
| 3019 | Не включен СБП в личном кабинете | - | SBP not enabled in cabinet |

### SBP Transaction Limits
| Code | Message | Details | Context |
|------|---------|---------|---------|
| 3029 | Слишком много неудачных попыток за час | По требованиям НСПК в час допустимо проводить не больше 1 попытки возврата по операции | Hourly refund attempt limit |
| 3030 | Слишком много неудачных попыток за сутки | По требованиям НСПК в день допустимо проводить не больше 5 попыток возврата по операции | Daily refund attempt limit |
| 3041 | Слишком много неудачных попыток за сутки | - | Daily attempt limit exceeded |
| 3042 | Слишком много неудачных попыток за час | - | Hourly attempt limit exceeded |

### SBP Customer Bank Errors
| Code | Message | Context |
|------|---------|---------|
| 5060 | У покупателя недостаточно денежных средств для проведения операции | Insufficient customer funds |
| 5061 | Покупатель превысил лимит по сумме операций по СБП | Customer SBP amount limit exceeded |
| 5062 | Покупатель превысил лимит по количеству операций по СБП | Customer SBP count limit exceeded |
| 5063 | Банк покупателя отклонил операцию как подозрительную | Customer bank fraud detection |
| 5064 | Покупателю запрещено выполнение операций в данной категории магазинов | Merchant category restriction |
| 5065 | Счет покупателя заблокирован | Customer account blocked |
| 5066 | Счет покупателя закрыт | Customer account closed |
| 5067 | Банк покупателя отклонил операцию по требованию законодательства | Regulatory compliance rejection |

## BNPL and Installment Errors (8000-8999)

### Buy Now Pay Later Errors
| Code | Message | Context |
|------|---------|---------|
| 8001 | Операция запрещена для рассрочки | Installment operation forbidden |
| 8002 | I-Bank Credit Broker недоступен. Повторите попытку позже | Credit broker unavailable |
| 8003 | Операция запрещена для покупки долями | BNPL operation forbidden |
| 8004 | BNPL недоступен. Повторите попытку позже | BNPL service unavailable |

## System Errors (9000-9999)

### Critical System Errors
| Code | Message | Context |
|------|---------|---------|
| 9001 | Попробуйте повторить попытку позже | Temporary system issue |
| 9999 | Внутренняя ошибка системы | Internal system error |

## Receipt-Specific Validation Errors

### Marked Goods Validation
| Error Message | Context |
|---------------|---------|
| Количество товара должно быть больше нуля | Item quantity validation |
| Максимальная длина rawcode — 223 символа | Raw code length validation |
| Касса принимает значение markCode только с типом rawcode | Mark code type validation |
| Для данной кассы ожидалось одно из itemCode или markСode | Code requirement validation |
| Для данной кассы ожидалось либо itemCode, либо markCode | Exclusive code validation |
| Для данной кассы предусмотрена передача только markСode | Mark code only validation |

## Error Handling Best Practices

### Error Categories by Severity

#### Critical Errors (Immediate Action Required)
- System errors (3, 401, 9999)
- Terminal blocked (202)
- Invalid authentication (204, 205)

#### User Action Required
- Insufficient funds (103, 116, 1051)
- Card expired (252, 1033, 1054)
- Invalid card data (1014, 1015, 1082)

#### Configuration Issues
- Terminal not configured (3001, 3019)
- Payment method unavailable (13, 1099)
- Certificate issues (411-417)

#### Temporary Issues (Retry Recommended)
- Service unavailable (55, 120, 123, 125, 402)
- Bank processing issues (99, 1017, 1030, 1034, 1089)
- External service errors (96, 97, 98)

#### Business Logic Errors
- Invalid state transitions (4, 8)
- Amount validation (251, 308, 334)
- Limit exceeded (119, 403-406, 1013, 1061, 1065)

### Error Response Pattern

#### Standard Error Structure
```json
{
  "Success": false,
  "ErrorCode": "1051",
  "Message": "Недостаточно средств на карте",
  "Details": "Не получилось оплатить. На карте недостаточно средств",
  "Status": "REJECTED"
}
```

#### Error Context Enhancement
```json
{
  "Success": false,
  "ErrorCode": "204",
  "Message": "Неверный токен. Проверьте пару TerminalKey/SecretKey",
  "Details": "Token validation failed",
  "Status": "ERROR",
  "ErrorContext": {
    "category": "AUTHENTICATION",
    "severity": "CRITICAL",
    "retry_possible": false,
    "user_action_required": true,
    "support_contact": true
  }
}
```

### Integration Guidelines

#### Error Handling Strategy by API Method

**Init Method Error Handling:**
- 204, 205: Check terminal credentials
- 201, 202: Validate required parameters
- 251: Verify amount format and minimum
- 253: Check currency support

**CheckOrder Method Error Handling:**
- 255: Payment not found - verify PaymentId
- 335: Order not found - verify OrderId/TerminalKey pair
- 204: Authentication failure

**Cancel Method Error Handling:**
- 4, 8: Invalid payment status for cancellation
- 330: Cancel amount exceeds original
- 3002: Insufficient merchant balance for refund
- 100: Multiple cancellation scenarios

**Confirm Method Error Handling:**
- 4: Payment not in AUTHORIZED status
- 330: Confirm amount exceeds authorized
- 100: Payment already confirmed
- 255: Payment not found

#### Client-Side Error Handling

**Critical Errors (Stop Processing):**
```javascript
const criticalErrors = ['3', '202', '204', '205', '501', '648'];
if (criticalErrors.includes(response.ErrorCode)) {
    // Stop processing, contact support
    showCriticalError(response.Message);
    return;
}
```

**Retry Logic:**
```javascript
const retryableErrors = ['55', '120', '123', '125', '402', '999', '1017', '1030'];
if (retryableErrors.includes(response.ErrorCode)) {
    // Implement exponential backoff retry
    setTimeout(() => retryOperation(), getBackoffDelay());
}
```

**User Action Required:**
```javascript
const userActionErrors = ['103', '116', '252', '1014', '1015', '1051', '1082'];
if (userActionErrors.includes(response.ErrorCode)) {
    // Show user-friendly message with action required
    showUserActionRequired(response.Message);
}
```

#### Server-Side Error Monitoring

**Alert Thresholds:**
- Error rate > 5% for critical errors (3, 401, 9999)
- Authentication failure rate > 10% (204, 205)
- Payment failure rate > 15% (100-199 range)
- SBP error rate > 20% (3000-5999 range)

**Error Categorization for Monitoring:**
```json
{
  "error_categories": {
    "system_errors": ["3", "401", "9999"],
    "authentication_errors": ["204", "205"],
    "validation_errors": ["201-262", "308-386"],
    "bank_rejections": ["1000-1999"],
    "insufficient_funds": ["103", "116", "1051"],
    "card_issues": ["252", "506", "508", "1014", "1015"],
    "limit_exceeded": ["403-406", "1013", "1061", "1204"],
    "sbp_errors": ["3000-5999"],
    "receipt_errors": ["308-386"]
  }
}
```

### Localization and User Experience

#### Error Message Localization
```json
{
  "error_translations": {
    "ru": {
      "1051": "Недостаточно средств на карте",
      "1082": "Неверный CVV код"
    },
    "en": {
      "1051": "Insufficient funds on card",
      "1082": "Invalid CVV code"
    }
  }
}
```

#### User-Friendly Error Mapping
```json
{
  "user_friendly_messages": {
    "1051": {
      "title": "Недостаточно средств",
      "message": "На вашей карте недостаточно средств для совершения покупки",
      "action": "Попробуйте другую карту или пополните счет",
      "icon": "warning"
    },
    "1082": {
      "title": "Неверный код безопасности",
      "message": "Проверьте правильность ввода CVV/CVC кода",
      "action": "Введите трехзначный код с обратной стороны карты",
      "icon": "error"
    }
  }
}
```

### Error Analytics and Reporting

#### Key Error Metrics
- **Error Rate by Category**: System/Authentication/Validation/Bank
- **Error Distribution by Payment Method**: Card/SBP/BNPL
- **Geographic Error Patterns**: Foreign cards restrictions
- **Temporal Error Patterns**: Peak hours error rates
- **Merchant-Specific Errors**: Terminal configuration issues

#### Error Correlation Analysis
```json
{
  "error_correlations": {
    "high_3ds_failures": {
      "related_errors": ["101", "106", "108", "511"],
      "common_cause": "3DS not properly configured"
    },
    "foreign_card_issues": {
      "related_errors": ["76", "77", "78", "79"],
      "common_cause": "Foreign card restrictions"
    },
    "receipt_validation_cluster": {
      "related_errors": ["308", "309", "334"],
      "common_cause": "Fiscal integration issues"
    }
  }
}
```

### Compliance and Regulatory Considerations

#### PCI DSS Compliance
- Never log sensitive data in error messages
- Mask card numbers in error contexts
- Secure error log storage and access

#### Financial Regulations
- Maintain audit trail of all error occurrences
- Report suspicious activity patterns (fraud-related errors)
- Comply with data retention requirements for error logs

#### Customer Protection
- Provide clear, actionable error messages
- Avoid exposing internal system details
- Offer alternative payment methods when possible

This comprehensive error code specification provides the foundation for robust error handling across the entire I-Business payment lifecycle, ensuring proper system monitoring, user experience optimization, and regulatory compliance.