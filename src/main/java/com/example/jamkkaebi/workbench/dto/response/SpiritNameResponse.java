package com.example.jamkkaebi.workbench.dto.response;

import com.example.jamkkaebi.relic.dto.response.RelicCard;

// 이름을 바꾼 뒤의 카드. 목록을 다시 조회하지 않고 갱신
public record SpiritNameResponse(RelicCard relic) {
}
