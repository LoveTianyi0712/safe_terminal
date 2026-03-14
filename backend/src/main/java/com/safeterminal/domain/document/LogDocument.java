package com.safeterminal.domain.document;

import lombok.Builder;
import lombok.Data;
import org.springframework.data.annotation.Id;
import org.springframework.data.elasticsearch.annotations.*;

import java.time.Instant;
import java.util.Map;

/**
 * Elasticsearch 日志文档
 * 索引名按天生成：logs-2025.04.07
 */
@Data
@Builder
@Document(indexName = "#{@indexNameResolver.resolveLogIndex()}")
@Setting(shards = 3, replicas = 1)
public class LogDocument {

    @Id
    private String id;

    @Field(type = FieldType.Keyword)
    private String terminalId;

    @Field(type = FieldType.Keyword)
    private String hostname;

    @Field(type = FieldType.Keyword)
    private String ipAddress;

    @Field(type = FieldType.Integer)
    private Integer os;

    @Field(type = FieldType.Date, format = DateFormat.epoch_millis)
    private Instant timestamp;

    /** 0=INFO 1=WARNING 2=ERROR 3=CRITICAL */
    @Field(type = FieldType.Integer)
    private Integer severity;

    @Field(type = FieldType.Keyword)
    private String source;

    @Field(type = FieldType.Keyword)
    private String eventId;

    @Field(type = FieldType.Text, analyzer = "ik_max_word")
    private String message;

    @Field(type = FieldType.Object, enabled = false)
    private Map<String, String> extra;
}
