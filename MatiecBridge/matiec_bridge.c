#include "iec_types_all.h"
#include "POUS.h"
extern void config_init__(void);
extern void config_run__(unsigned long tick);
static IEC_BOOL in_bits[64], out_bits[64];

void plc_init(void)               { config_init__(); /* bind __IX/__QX pointers to in_bits/out_bits */ }
void plc_scan(unsigned long tick) { config_run__(tick); }
IEC_BOOL* plc_inputs(void)        { return in_bits; }
IEC_BOOL* plc_outputs(void)       { return out_bits; }