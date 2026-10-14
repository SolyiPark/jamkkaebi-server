package com.example.jamkkaebi.exhibition.dto.request;

import com.example.jamkkaebi.exhibition.domain.ExhibitItemType;
import jakarta.validation.Valid;
import jakarta.validation.constraints.NotNull;

import java.util.List;

/**
 * 전시관 배치 저장. <b>전체 배치를 한 번에</b> 보낸다 — 목록에 없는 항목은 전시관에서 내려간다.
 *
 * <p>좌표가 그리드 안인지와 중복 검사는 그리드 크기가 서비스에서 한다.
 *
 * @param items 배치할 항목 전체. 빈 배열이면 전부 내린다
 */
public record ExhibitionLayoutRequest(

        @NotNull(message = "필수 값입니다.")
        List<@NotNull(message = "필수 값입니다.") @Valid Item> items
) {

    /**
     * @param type {@code SPIRIT} 이면 {@code id} 는 {@code relicId}, {@code BUILDING} 이면 {@code buildingId}
     */
    public record Item(

            @NotNull(message = "필수 값입니다.")
            ExhibitItemType type,

            @NotNull(message = "필수 값입니다.")
            Integer id,

            @NotNull(message = "필수 값입니다.")
            Integer x,

            @NotNull(message = "필수 값입니다.")
            Integer y
    ) {
    }
}
