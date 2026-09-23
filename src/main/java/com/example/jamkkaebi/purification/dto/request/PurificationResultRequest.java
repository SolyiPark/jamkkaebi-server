package com.example.jamkkaebi.purification.dto.request;

import com.example.jamkkaebi.purification.domain.FailReason;
import com.example.jamkkaebi.purification.domain.PlayResult;
import jakarta.validation.Valid;
import jakarta.validation.constraints.Max;
import jakarta.validation.constraints.Min;
import jakarta.validation.constraints.NotNull;

/**
 * 한 판의 결과 보고.
 */
public record PurificationResultRequest(

        @NotNull(message = "필수 값입니다.")
        PlayResult result,

        // 실패일 때만 보냄
        FailReason failReason,

        @NotNull(message = "필수 값입니다.")
        @Min(value = 0, message = "0 이상이어야 합니다.")
        Integer tapsUsed,

        @NotNull(message = "필수 값입니다.")
        @Min(value = 0, message = "0 이상이어야 합니다.")
        Integer scoutsUsed,

        @NotNull(message = "필수 값입니다.")
        @Min(value = 0, message = "0 이상이어야 합니다.")
        @Max(value = 100, message = "100을 넘을 수 없습니다.")
        Integer damage,

        @Valid
        BonusTiles bonusTiles
) {

    public PurificationResultRequest {
        bonusTiles = bonusTiles == null ? BonusTiles.NONE : bonusTiles;
    }

    // 캐낸 보너스 타일 수
    public record BonusTiles(

            @Min(value = 0, message = "0 이상이어야 합니다.")
            Integer jeongseong,

            @Min(value = 0, message = "0 이상이어야 합니다.")
            Integer eraCrystal
    ) {

        public static final BonusTiles NONE = new BonusTiles(0, 0);

        public BonusTiles {
            jeongseong = jeongseong == null ? 0 : jeongseong;
            eraCrystal = eraCrystal == null ? 0 : eraCrystal;
        }
    }
}
