package metrics

import (
	"net/http"

	"github.com/prometheus/client_golang/prometheus"
	"github.com/prometheus/client_golang/prometheus/promauto"
	"github.com/prometheus/client_golang/prometheus/promhttp"
	"go.uber.org/zap"
)

var (
	ActiveConnections = promauto.NewGauge(prometheus.GaugeOpts{
		Name: "st_gateway_active_connections",
		Help: "Number of currently active gRPC connections",
	})

	ReportsReceived = promauto.NewCounterVec(prometheus.CounterOpts{
		Name: "st_gateway_reports_received_total",
		Help: "Total number of reports received from agents",
	}, []string{"type"})

	KafkaWriteErrors = promauto.NewCounter(prometheus.CounterOpts{
		Name: "st_gateway_kafka_write_errors_total",
		Help: "Total Kafka write errors",
	})

	ReportProcessLatency = promauto.NewHistogram(prometheus.HistogramOpts{
		Name:    "st_gateway_report_process_duration_seconds",
		Help:    "Latency of processing a single agent report",
		Buckets: prometheus.DefBuckets,
	})
)

// StartMetricsServer 启动 Prometheus HTTP metrics 端点
func StartMetricsServer(addr string, log *zap.Logger) {
	mux := http.NewServeMux()
	mux.Handle("/metrics", promhttp.Handler())
	mux.HandleFunc("/healthz", func(w http.ResponseWriter, _ *http.Request) {
		w.WriteHeader(http.StatusOK)
		_, _ = w.Write([]byte("ok"))
	})

	log.Info("metrics server listening", zap.String("addr", addr))
	if err := http.ListenAndServe(addr, mux); err != nil {
		log.Error("metrics server error", zap.Error(err))
	}
}
