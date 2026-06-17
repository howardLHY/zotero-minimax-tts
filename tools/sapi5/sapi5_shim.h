// sapi5_shim.h
//
// The TTS engine interfaces live in the SDK's SAPI DDK header rather than
// sapi.h. Keeping this tiny wrapper lets the engine include one local file
// while still using the official SPVTEXTFRAG and ISpTTSEngine layouts.

#pragma once
#include <sapi.h>
#include <sapiddk.h>
