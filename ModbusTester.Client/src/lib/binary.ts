/**
 * SignalR's default JSON hub protocol (System.Text.Json) serializes C# byte[] as a base64
 * string, not a JSON array — ModbusHub's "Traffic" event therefore arrives as base64, not bytes.
 */
export function base64ToHexString(base64: string): string {
  const binary = atob(base64);
  const hexBytes = new Array<string>(binary.length);
  for (let i = 0; i < binary.length; i++) {
    hexBytes[i] = binary.charCodeAt(i).toString(16).padStart(2, "0");
  }
  return hexBytes.join(" ").toUpperCase();
}

export function toBinaryString16(value: number): string {
  const bits = (value & 0xffff).toString(2).padStart(16, "0");
  return `${bits.slice(0, 4)} ${bits.slice(4, 8)} ${bits.slice(8, 12)} ${bits.slice(12, 16)}`;
}
