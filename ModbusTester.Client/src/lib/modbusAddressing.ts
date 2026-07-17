import type { ModbusFunctionCode } from "../types/modbus";

/**
 * Modicon-style addressing: the leading digit(s) identify the register/coil type, so the same
 * protocol address 0 is displayed as 40000 for holding registers, 30000 for input registers, etc.
 * Offsets are 0-based (protocol address 0 -> 40000) to match this tool's Start Address field,
 * rather than the classic 1-based 40001 convention.
 */
const ADDRESS_BASE_BY_FUNCTION_CODE: Record<ModbusFunctionCode, number> = {
  ReadCoils: 0,
  WriteSingleCoil: 0,
  WriteMultipleCoils: 0,
  ReadDiscreteInputs: 10000,
  ReadInputRegisters: 30000,
  ReadHoldingRegisters: 40000,
  WriteSingleRegister: 40000,
  WriteMultipleRegisters: 40000,
};

export function getAddressBase(functionCode: ModbusFunctionCode): number {
  return ADDRESS_BASE_BY_FUNCTION_CODE[functionCode];
}

export function toDisplayAddress(rawAddress: number, functionCode: ModbusFunctionCode): number {
  return rawAddress + getAddressBase(functionCode);
}

export function toRawAddress(displayAddress: number, functionCode: ModbusFunctionCode): number {
  return Math.max(0, displayAddress - getAddressBase(functionCode));
}
