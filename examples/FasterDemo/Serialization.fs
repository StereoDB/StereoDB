module FasterDemo.Serialization

let encodeMsgHeader (tableIndex: byte) (isRemoved: bool) =
    if tableIndex > 127uy then
        invalidArg "number" "Number must be in the range 0-127."
    else
        // Shift the boolean flag to the 7th bit and OR it with the number
        let flagBit = if isRemoved then 1uy <<< 7 else 0uy
        flagBit ||| (tableIndex &&& 0x7Fuy)

let decodeMsgHeader (encodedByte: byte) =
    // Extract the flag from the 7th bit and the number from the lower 7 bits
    let isRemoved = (encodedByte &&& 0x80uy) <> 0uy
    let tableIndex = encodedByte &&& 0x7Fuy
    struct (isRemoved, tableIndex)