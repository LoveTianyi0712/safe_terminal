package com.safeterminal.domain.document;

import lombok.Builder;
import lombok.Data;
import org.springframework.data.annotation.Id;
import org.springframework.data.elasticsearch.annotations.*;

import java.time.Instant;

@Data
@Builder
@Document(indexName = "#{@indexNameResolver.resolveUsbIndex()}")
@Setting(shards = 3, replicas = 1)
public class UsbEventDocument {

    @Id
    private String id;

    @Field(type = FieldType.Keyword)
    private String terminalId;

    @Field(type = FieldType.Keyword)
    private String hostname;

    @Field(type = FieldType.Date, format = DateFormat.epoch_millis)
    private Instant timestamp;

    /** 0=CONNECTED 1=DISCONNECTED */
    @Field(type = FieldType.Integer)
    private Integer action;

    @Field(type = FieldType.Keyword)
    private String deviceId;

    @Field(type = FieldType.Keyword)
    private String vendor;

    @Field(type = FieldType.Keyword)
    private String product;

    @Field(type = FieldType.Keyword)
    private String serialNumber;

    @Field(type = FieldType.Keyword)
    private String mountPoint;
}
