import { useState, useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  ComposedChart, Bar, Line, XAxis, YAxis, Tooltip, CartesianGrid,
  Cell, ResponsiveContainer, ReferenceLine, Legend
} from "recharts";
import axios from "axios";
import "./App.css";

interface HourData {
  hourStart: string;
  priceDKK: number;
  co2PerKwh: number;
  priceArea: string;
}

interface ChargeRecommendation {
  hourStart: string;
  priceDKK: number;
  co2PerKwh: number;
  isRecommended: boolean;
}

interface ChargeWindow {
  windowStart: string;
  windowEnd: string;
  totalCostDKK: number;
  averagePriceDKK: number;
  averageCo2: number;
  hours: ChargeRecommendation[];
}

type Strategy = "Cheapest" | "Greenest";

const API   = "http://localhost:5000/api/elspot";
const AREAS = ["DK1", "DK2"];

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const fetchJson = (url: string): Promise<any> => axios.get(url).then(r => r.data);

// Format ISO timestamp → "HH:mm"
const fmt = (iso: string) =>
  new Date(iso).toLocaleTimeString("da-DK", { hour: "2-digit", minute: "2-digit" });

// kr/kWh → øre/kWh
const toOere = (kr: number) => Math.round(kr * 100);

export default function App() {
  const [hours,    setHours]    = useState(4);
  const [area,     setArea]     = useState<string>("DK2");
  const [deadline, setDeadline] = useState("07:00");
  const [strategy, setStrategy] = useState<Strategy>("Cheapest");

  // Build deadline ISO — if deadline time has already passed today, use tomorrow
  const { deadlineISO, deadlineError } = useMemo(() => {
    const now = new Date();
    const parts = deadline.split(":");
    const h = parseInt(parts[0] ?? "", 10);
    const m = parseInt(parts[1] ?? "", 10);
    if (isNaN(h) || isNaN(m)) {
      const tomorrow = new Date(now);
      tomorrow.setDate(tomorrow.getDate() + 1);
      return { deadlineISO: tomorrow.toISOString(), deadlineError: null };
    }
    const d = new Date(now);
    d.setHours(h, m, 0, 0);
    const hoursUntil = (d.getTime() - now.getTime()) / 3600000;
    if (hoursUntil < hours) d.setDate(d.getDate() + 1);
    const finalHours = (d.getTime() - now.getTime()) / 3600000;
    const error = finalHours < hours
      ? `Kun ${finalHours.toFixed(1)} t til deadline — ikke nok tid til ${hours} timers opladning`
      : null;
    return { deadlineISO: d.toISOString(), deadlineError: error };
  }, [deadline, hours]);

  const { data: recommendations = [], isLoading, isError } = useQuery<ChargeRecommendation[]>({
    queryKey: ["recommendations", hours, area, strategy],
    queryFn:  () => fetchJson(`${API}/recommendations?hours=${hours}&area=${area}&strategy=${strategy}`),
  });

  const { data: merged = [] } = useQuery<HourData[]>({
    queryKey: ["merged", area],
    queryFn:  () => fetchJson(`${API}/merged?area=${area}`),
  });

  const { data: window } = useQuery<ChargeWindow | null>({
    queryKey: ["window", hours, area, deadlineISO, strategy],
    retry: false,
    enabled: !deadlineError,
    queryFn: () =>
      axios.get<ChargeWindow>(`${API}/window?hours=${hours}&area=${area}&deadline=${encodeURIComponent(deadlineISO)}&strategy=${strategy}`)
        .then(r => r.data)
        .catch(e => e?.response?.status === 404 ? null : Promise.reject(e)),
  });

  // Only show the most recent 24 hours
  const last24 = useMemo(() => {
    if (merged.length === 0) return [];
    const sorted = [...merged].sort((a, b) =>
      new Date(a.hourStart).getTime() - new Date(b.hourStart).getTime());
    return sorted.slice(-24);
  }, [merged]);

  const avgOere = last24.length ? Math.round(last24.reduce((s, d) => s + d.priceDKK, 0) / last24.length * 100) : 0;
  const avgCo2  = last24.length ? last24.reduce((s, d) => s + d.co2PerKwh, 0) / last24.length : 0;

  const chartData = last24.map(h => {
    const rec = recommendations.find(r => r.hourStart === h.hourStart);
    const inWin = window &&
      new Date(h.hourStart) >= new Date(window.windowStart) &&
      new Date(h.hourStart) < new Date(window.windowEnd);
    return {
      ...h,
      priceOere: toOere(h.priceDKK),   // display in øre
      isRecommended: rec?.isRecommended ?? false,
      inWindow: inWin ?? false,
    };
  });

  const isCheapest = strategy === "Cheapest";

  return (
    <div className="app">
      <header>
        <h1>⚡ SmartCharger</h1>
        <p>Opladningsanbefaling baseret på live elpriser og CO₂ fra Energinet</p>
      </header>

      <div className="strategy-toggle">
        <button className={isCheapest ? "active" : ""} onClick={() => setStrategy("Cheapest")}>
          💰 Billigst
        </button>
        <button className={!isCheapest ? "active green" : "green"} onClick={() => setStrategy("Greenest")}>
          🌿 Grønnest
        </button>
      </div>

      <div className="controls">
        <label>
          Prisområde:
          <select value={area} onChange={e => setArea(e.target.value)}>
            {AREAS.map(a => <option key={a}>{a}</option>)}
          </select>
        </label>
        <label>
          Timer der skal oplades:
          <input type="number" min={1} max={12} value={hours}
            onChange={e => setHours(Number(e.target.value))} />
        </label>
        <label>
          Bilen skal køre kl.:
          <input
            type="time"
            value={deadline}
            className={deadlineError ? "input-error" : ""}
            onChange={e => setDeadline(e.target.value)}
          />
        </label>
      </div>

      {deadlineError && <p className="error">⚠️ {deadlineError}</p>}
      {isError      && <p className="error">Kunne ikke hente data — prøv igen om lidt.</p>}

      {isLoading ? (
        <p className="loading">Henter priser…</p>
      ) : (
        <>
          {window && (
            <div className={`window-banner ${!isCheapest ? "green-banner" : ""}`}>
              <strong>Bedste {isCheapest ? "billigste" : "grønneste"} vindue:</strong>{" "}
              {fmt(window.windowStart)} – {fmt(window.windowEnd)}{" · "}
              {isCheapest
                ? `gns. ${toOere(window.averagePriceDKK)} øre/kWh`
                : `gns. ${window.averageCo2.toFixed(0)} g CO₂/kWh`}
            </div>
          )}

          <div className="summary">
            <div className="card">
              <span>{avgOere} øre</span>
              <small>Gns. spotpris/kWh</small>
            </div>
            <div className="card green">
              <span>{avgCo2.toFixed(0)} g</span>
              <small>Gns. CO₂/kWh</small>
            </div>
            {window && (
              <div className={`card ${isCheapest ? "blue" : "emerald"}`}>
                <span>{fmt(window.windowStart)}</span>
                <small>Start optimalt vindue</small>
              </div>
            )}
          </div>

          <div className="chart-wrapper">
          <ResponsiveContainer width="100%" height={280}>
            <ComposedChart data={chartData} margin={{ top: 8, right: 50, left: 10, bottom: 70 }}>
              <CartesianGrid strokeDasharray="3 3" />
              <XAxis
                dataKey="hourStart"
                tickFormatter={fmt}
                angle={-45}
                textAnchor="end"
                interval={3}
                height={60}
              />
              <YAxis
                yAxisId="price"
                tickFormatter={v => `${v} ø`}
                width={55}
              />
              <YAxis
                yAxisId="co2"
                orientation="right"
                tickFormatter={v => `${v} g`}
                domain={["auto", "auto"]}
                width={55}
              />
              <Tooltip
                formatter={(v, name) => {
                  const val = Number(v);
                  return name === "CO₂"
                    ? [`${val.toFixed(0)} g CO₂/kWh`, "CO₂"]
                    : [`${val} øre/kWh`, "Spotpris"];
                }}
                labelFormatter={l => new Date(l).toLocaleString("da-DK")}
              />
              <Legend verticalAlign="bottom" height={36} wrapperStyle={{ paddingTop: "12px" }} />
              <ReferenceLine yAxisId="price" y={avgOere} stroke="#888" strokeDasharray="4 4" />
              <Bar yAxisId="price" dataKey="priceOere" radius={[4, 4, 0, 0]} name="Spotpris">
                {chartData.map((entry, i) => (
                  <Cell key={i} fill={
                    entry.inWindow      ? (isCheapest ? "#22c55e" : "#10b981") :
                    entry.isRecommended ? "#86efac" : "#64748b"
                  } />
                ))}
              </Bar>
              <Line yAxisId="co2" type="monotone" dataKey="co2PerKwh"
                stroke="#f59e0b" strokeWidth={2} dot={false} name="CO₂" />
            </ComposedChart>
          </ResponsiveContainer>
          </div>

          <p className="legend">
            <span className="dot green" /> Optimalt vindue &nbsp;
            <span className="dot lightgreen" /> Anbefalet time &nbsp;
            <span className="dot grey" /> Øvrige timer
          </p>
        </>
      )}
    </div>
  );
}
