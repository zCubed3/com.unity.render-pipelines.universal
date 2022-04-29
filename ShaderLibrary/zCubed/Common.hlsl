#ifndef UNIVERSAL_PIPELINE_ZCUBED_COMMON_INCLUDED
#define UNIVERSAL_PIPELINE_ZCUBED_COMMON_INCLUDED

// Old Unity function that doesn't exist in SRPs for some reason
half LerpOneTo(half b, half t)
{
    half oneMinusT = 1 - t;
    return oneMinusT + b * t;
}

#endif