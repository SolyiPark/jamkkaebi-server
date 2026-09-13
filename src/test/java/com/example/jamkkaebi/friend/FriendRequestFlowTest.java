package com.example.jamkkaebi.friend;

import com.example.jamkkaebi.friend.domain.Block;
import com.example.jamkkaebi.friend.domain.Friendship;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

import java.time.Duration;
import java.time.LocalDateTime;
import java.util.Locale;

import static org.assertj.core.api.Assertions.assertThat;
import static org.hamcrest.Matchers.startsWith;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.header;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

/**
 * 친구 요청 · 수락 · 거절 · 취소 · 만료 · 상한 · 차단 · 코드 조회 한도를 HTTP 로 확인한다.
 */
class FriendRequestFlowTest extends FriendApiTestSupport {

    /** 요청 상한 테스트용 가짜 사용자 id. 실제 사용자와 겹치지 않을 만큼 크다. */
    private static final long FAKE_USER_ID_BASE = 900_000L;

    @Test
    @DisplayName("요청을 보내고 받은 쪽이 수락하면 양쪽 목록에 친구로 뜬다")
    void requestThenAccept() throws Exception {
        Player alice = player("앨리스");
        Player bob = player("밥이");
        String typedByHand = (bob.code().substring(0, 4) + "-" + bob.code().substring(4)).toLowerCase(Locale.ROOT);

        sendRequest(alice, typedByHand)
                .andExpect(status().isCreated())
                .andExpect(jsonPath("$.code").value("FRIEND_REQUEST_SENT"))
                .andExpect(jsonPath("$.data.relation").value("REQUEST_SENT"))
                .andExpect(jsonPath("$.data.player.friendCode").value(bob.code()))
                .andExpect(jsonPath("$.data.expiresAt").value(startsWith("2026-09-26T10:00")));

        get(bob, "/api/friend-requests?direction=RECEIVED")
                .andExpect(jsonPath("$.data.count").value(1))
                .andExpect(jsonPath("$.data.limit").value(20))
                .andExpect(jsonPath("$.data.requests[0].player.friendCode").value(alice.code()));

        post(bob, "/api/friend-requests/{code}/accept", alice.code())
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value("FRIEND_ADDED"))
                .andExpect(jsonPath("$.data.relation").value("FRIEND"))
                .andExpect(jsonPath("$.data.expiresAt").doesNotExist());

        get(alice, "/api/friends")
                .andExpect(jsonPath("$.data.count").value(1))
                .andExpect(jsonPath("$.data.friends[0].friendCode").value(bob.code()));
        get(bob, "/api/friends")
                .andExpect(jsonPath("$.data.friends[0].friendCode").value(alice.code()));
        assertThat(friendRequestRepository.count()).isZero();
    }

    @Test
    @DisplayName("서로에게 요청하면 뒤에 도착한 요청이 자동 수락된다")
    void crossingRequestsBecomeFriends() throws Exception {
        Player alice = player("앨리스");
        Player bob = player("밥이");

        sendRequest(alice, bob.code()).andExpect(status().isCreated());
        sendRequest(bob, alice.code())
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value("FRIEND_ADDED"));

        assertThat(friendshipRepository.count()).isEqualTo(1);
        assertThat(friendRequestRepository.count()).isZero();
    }

    @Test
    @DisplayName("대기 중인 요청을 다시 보내면 새로 만들지 않고 기존 요청을 200 으로 돌려준다")
    void resendingOutstandingRequestReturnsExistingOne() throws Exception {
        Player alice = player("앨리스");
        Player bob = player("밥이");
        sendRequest(alice, bob.code())
                .andExpect(status().isCreated())
                .andExpect(jsonPath("$.data.expiresAt").value(startsWith("2026-09-26T10:00")));

        clock.advance(Duration.ofHours(1));

        sendRequest(alice, bob.code())
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value("FRIEND_REQUEST_SENT"))
                .andExpect(jsonPath("$.data.relation").value("REQUEST_SENT"))
                .andExpect(jsonPath("$.data.expiresAt").value(startsWith("2026-09-26T10:00")));
        assertThat(friendRequestRepository.count()).isEqualTo(1);
    }

    @Test
    @DisplayName("거절은 보낸 쪽에 드러나지 않는다 — 보낸 요청함에 남고, 다시 보내도 같은 응답이다")
    void rejectionIsHiddenFromSender() throws Exception {
        Player alice = player("앨리스");
        Player bob = player("밥이");
        sendRequest(alice, bob.code()).andExpect(status().isCreated());

        post(bob, "/api/friend-requests/{code}/reject", alice.code())
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value("FRIEND_REQUEST_REJECTED"));

        get(bob, "/api/friend-requests?direction=RECEIVED").andExpect(jsonPath("$.data.count").value(0));
        get(alice, "/api/friend-requests?direction=SENT")
                .andExpect(jsonPath("$.data.count").value(1))
                .andExpect(jsonPath("$.data.requests[0].player.friendCode").value(bob.code()));
        get(alice, "/api/friends/lookup/{code}", bob.code())
                .andExpect(jsonPath("$.data.relation").value("REQUEST_SENT"));

        // 거절되지 않은 요청을 다시 보냈을 때와 같은 200 응답이어야 거절 사실이 드러나지 않는다.
        sendRequest(alice, bob.code())
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value("FRIEND_REQUEST_SENT"))
                .andExpect(jsonPath("$.data.relation").value("REQUEST_SENT"));
        assertThat(friendRequestRepository.count()).isEqualTo(1);

        // 거절했던 쪽이 마음을 바꿔 요청하면, 서로 원한다는 것이 확인됐으므로 곧바로 친구가 된다.
        sendRequest(bob, alice.code()).andExpect(jsonPath("$.code").value("FRIEND_ADDED"));
    }

    @Test
    @DisplayName("보낸 요청을 취소하면 양쪽 요청함에서 사라지고 보낸 요청 칸이 비워진다")
    void cancelReclaimsSentSlot() throws Exception {
        Player alice = player("앨리스");
        Player bob = player("밥이");
        sendRequest(alice, bob.code()).andExpect(status().isCreated());

        delete(alice, "/api/friend-requests/{code}", bob.code())
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value("FRIEND_REQUEST_CANCELED"))
                .andExpect(jsonPath("$.data.relation").value("NONE"))
                .andExpect(jsonPath("$.data.sentCount").value(0))
                .andExpect(jsonPath("$.data.limit").value(10));

        get(alice, "/api/friend-requests?direction=SENT").andExpect(jsonPath("$.data.count").value(0));
        get(bob, "/api/friend-requests?direction=RECEIVED").andExpect(jsonPath("$.data.count").value(0));
        get(alice, "/api/friends/lookup/{code}", bob.code())
                .andExpect(jsonPath("$.data.relation").value("NONE"));
        assertThat(friendRequestRepository.count()).isZero();

        // 취소한 상대에게 다시 보낼 수 있다.
        sendRequest(alice, bob.code()).andExpect(status().isCreated());
    }

    @Test
    @DisplayName("거절당한 요청도 취소로 칸을 되찾는다 — 거절 사실은 여전히 드러나지 않는다")
    void cancelReclaimsSlotTakenByRejectedRequest() throws Exception {
        Player alice = player("앨리스");
        Player bob = player("밥이");
        sendRequest(alice, bob.code()).andExpect(status().isCreated());
        post(bob, "/api/friend-requests/{code}/reject", alice.code()).andExpect(status().isOk());

        get(alice, "/api/friend-requests?direction=SENT").andExpect(jsonPath("$.data.count").value(1));

        delete(alice, "/api/friend-requests/{code}", bob.code())
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.relation").value("NONE"))
                .andExpect(jsonPath("$.data.sentCount").value(0));
        get(alice, "/api/friend-requests?direction=SENT").andExpect(jsonPath("$.data.count").value(0));
    }

    @Test
    @DisplayName("취소할 요청이 없어도, 없는 코드여도 같은 성공 응답이다 — 취소로는 아무것도 알 수 없다")
    void cancelTellsNothingApart() throws Exception {
        Player alice = player("앨리스");
        Player bob = player("밥이");
        for (int i = 0; i < 3; i++) {
            saveRequest(alice.id(), FAKE_USER_ID_BASE + i);
        }

        // ① 보낸 적 없는 상대  ② 존재하지 않는 코드 — 응답이 글자 그대로 같아야 한다.
        delete(alice, "/api/friend-requests/{code}", bob.code())
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value("FRIEND_REQUEST_CANCELED"))
                .andExpect(jsonPath("$.data.relation").value("NONE"))
                .andExpect(jsonPath("$.data.sentCount").value(3));
        delete(alice, "/api/friend-requests/{code}", "ZZZZZZZZ")
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.relation").value("NONE"))
                .andExpect(jsonPath("$.data.sentCount").value(3));

        // 형식이 틀렸거나 내 코드면 그제야 C001.
        delete(alice, "/api/friend-requests/{code}", "SHORT")
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("C001"))
                .andExpect(jsonPath("$.errors[0].field").value("friendCode"));
        delete(alice, "/api/friend-requests/{code}", alice.code())
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("C001"));
    }

    @Test
    @DisplayName("취소하는 사이 상대가 수락했으면 FRIEND 로 알려 주고 친구 관계는 건드리지 않는다")
    void cancelAfterAcceptKeepsFriendship() throws Exception {
        Player alice = player("앨리스");
        Player bob = player("밥이");
        sendRequest(alice, bob.code()).andExpect(status().isCreated());
        post(bob, "/api/friend-requests/{code}/accept", alice.code()).andExpect(status().isOk());

        delete(alice, "/api/friend-requests/{code}", bob.code())
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.relation").value("FRIEND"));

        assertThat(friendshipRepository.count()).isEqualTo(1);
        get(alice, "/api/friends").andExpect(jsonPath("$.data.count").value(1));
    }

    @Test
    @DisplayName("보낸 요청이 가득 차도 하나 취소하면 다시 보낼 수 있다")
    void cancelFreesRoomWhenSentRequestsAreFull() throws Exception {
        Player alice = player("앨리스");
        Player bob = player("밥이");
        Player carol = player("캐럴");
        sendRequest(alice, bob.code()).andExpect(status().isCreated());
        for (int i = 0; i < 9; i++) {
            saveRequest(alice.id(), FAKE_USER_ID_BASE + i);
        }

        sendRequest(alice, carol.code())
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("F003"));

        delete(alice, "/api/friend-requests/{code}", bob.code())
                .andExpect(jsonPath("$.data.sentCount").value(9));
        sendRequest(alice, carol.code()).andExpect(status().isCreated());
    }

    @Test
    @DisplayName("요청은 14일이 지나면 만료되어 수락할 수 없고, 다시 보낼 수 있다")
    void requestExpiresAfterFourteenDays() throws Exception {
        Player alice = player("앨리스");
        Player bob = player("밥이");
        sendRequest(alice, bob.code()).andExpect(status().isCreated());

        clock.advance(Duration.ofDays(14));

        get(bob, "/api/friend-requests?direction=RECEIVED").andExpect(jsonPath("$.data.count").value(0));
        get(alice, "/api/friend-requests?direction=SENT").andExpect(jsonPath("$.data.count").value(0));
        post(bob, "/api/friend-requests/{code}/accept", alice.code())
                .andExpect(status().isNotFound())
                .andExpect(jsonPath("$.code").value("F007"));

        sendRequest(alice, bob.code()).andExpect(status().isCreated());
        assertThat(friendRequestRepository.count()).isEqualTo(1);
    }

    @Test
    @DisplayName("상대의 받은 요청함이 20건이면 요청이 실패한다")
    void rejectsWhenReceiverInboxIsFull() throws Exception {
        Player alice = player("앨리스");
        Player bob = player("밥이");
        for (int i = 0; i < 20; i++) {
            saveRequest(FAKE_USER_ID_BASE + i, bob.id());
        }

        sendRequest(alice, bob.code())
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("F004"));
    }

    @Test
    @DisplayName("보낸 요청이 10건이면 더 보낼 수 없다")
    void rejectsWhenSentRequestsAreFull() throws Exception {
        Player alice = player("앨리스");
        Player bob = player("밥이");
        for (int i = 0; i < 10; i++) {
            saveRequest(alice.id(), FAKE_USER_ID_BASE + i);
        }

        sendRequest(alice, bob.code())
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("F003"));
    }

    @Test
    @DisplayName("내 친구가 30명이거나 상대 친구가 30명이면 요청이 실패한다")
    void rejectsWhenFriendListIsFull() throws Exception {
        Player alice = player("앨리스");
        Player bob = player("밥이");
        Player carol = player("캐럴");
        for (int i = 0; i < 30; i++) {
            friendshipRepository.save(Friendship.between(alice.id(), FAKE_USER_ID_BASE + i));
            friendshipRepository.save(Friendship.between(bob.id(), FAKE_USER_ID_BASE + 100 + i));
        }

        sendRequest(alice, carol.code()).andExpect(jsonPath("$.code").value("F005"));
        sendRequest(carol, bob.code()).andExpect(jsonPath("$.code").value("F006"));
    }

    @Test
    @DisplayName("이미 친구에게 요청하면 F011, 내 코드면 C001, 없는 코드면 F001")
    void rejectsInvalidTargets() throws Exception {
        Player alice = player("앨리스");
        Player bob = player("밥이");
        makeFriends(alice, bob);

        sendRequest(alice, bob.code())
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value("F011"));
        sendRequest(alice, alice.code())
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value("C001"))
                .andExpect(jsonPath("$.errors[0].field").value("friendCode"));
        sendRequest(alice, "ZZZZZZZZ")
                .andExpect(status().isNotFound())
                .andExpect(jsonPath("$.code").value("F001"));
    }

    @Test
    @DisplayName("친구를 삭제하면 양쪽에서 동시에 끊기고, 다시 삭제해도 성공이다")
    void removesFriendBothWays() throws Exception {
        Player alice = player("앨리스");
        Player bob = player("밥이");
        makeFriends(alice, bob);

        delete(bob, "/api/friends/{code}", alice.code())
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value("FRIEND_REMOVED"));
        get(alice, "/api/friends").andExpect(jsonPath("$.data.count").value(0));
        delete(bob, "/api/friends/{code}", alice.code()).andExpect(status().isOk());
    }

    @Test
    @DisplayName("차단하면 친구·요청이 끊기고, 어느 쪽에서 조회해도 존재하지 않는 코드가 된다")
    void blockHidesUsersFromEachOther() throws Exception {
        Player alice = player("앨리스");
        Player bob = player("밥이");
        makeFriends(alice, bob);
        saveRequest(bob.id(), alice.id());

        postCode(alice, "/api/blocks", bob.code())
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value("USER_BLOCKED"));

        assertThat(friendshipRepository.count()).isZero();
        assertThat(friendRequestRepository.count()).isZero();
        get(bob, "/api/friends/lookup/{code}", alice.code()).andExpect(jsonPath("$.code").value("F001"));
        get(alice, "/api/friends/lookup/{code}", bob.code()).andExpect(jsonPath("$.code").value("F001"));
        sendRequest(bob, alice.code()).andExpect(jsonPath("$.code").value("F001"));
        get(alice, "/api/blocks").andExpect(jsonPath("$.data.blocks[0].friendCode").value(bob.code()));

        delete(alice, "/api/blocks/{code}", bob.code())
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value("USER_UNBLOCKED"));
        get(bob, "/api/friends/lookup/{code}", alice.code())
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.relation").value("NONE"));
    }

    @Test
    @DisplayName("없는 코드를 차단해도 성공이다 — 차단 API 로 코드 존재 여부를 알 수 없어야 한다")
    void blockingUnknownCodeIsSilent() throws Exception {
        Player alice = player("앨리스");

        postCode(alice, "/api/blocks", "ZZZZZZZZ").andExpect(status().isOk());

        assertThat(blockRepository.count()).isZero();
    }

    @Test
    @DisplayName("코드 조회는 분당 10회 — 넘으면 429 와 Retry-After, 요청 보내기도 같은 한도를 쓴다")
    void limitsLookupsPerMinute() throws Exception {
        Player alice = player("앨리스");
        Player bob = player("밥이");
        for (int i = 0; i < 10; i++) {
            get(alice, "/api/friends/lookup/{code}", bob.code()).andExpect(status().isOk());
        }

        get(alice, "/api/friends/lookup/{code}", bob.code())
                .andExpect(status().isTooManyRequests())
                .andExpect(jsonPath("$.code").value("F002"))
                .andExpect(header().string("Retry-After", "60"));
        sendRequest(alice, bob.code()).andExpect(jsonPath("$.code").value("F002"));

        clock.advance(Duration.ofMinutes(1));
        get(alice, "/api/friends/lookup/{code}", bob.code()).andExpect(status().isOk());
    }

    @Test
    @DisplayName("코드 조회는 하루 100회 — 자정이 지나면 다시 조회할 수 있다")
    void limitsLookupsPerDayUntilMidnight() throws Exception {
        Player alice = player("앨리스");
        Player bob = player("밥이");
        for (int minute = 0; minute < 10; minute++) {
            for (int i = 0; i < 10; i++) {
                get(alice, "/api/friends/lookup/{code}", bob.code()).andExpect(status().isOk());
            }
            clock.advance(Duration.ofMinutes(1));
        }

        get(alice, "/api/friends/lookup/{code}", bob.code())
                .andExpect(status().isTooManyRequests())
                .andExpect(jsonPath("$.code").value("F002"));

        clock.set(LocalDateTime.of(2026, 9, 13, 0, 0, 1));
        get(alice, "/api/friends/lookup/{code}", bob.code()).andExpect(status().isOk());
    }

    @Test
    @DisplayName("내 코드를 조회하면 SELF 다")
    void lookupOwnCodeIsSelf() throws Exception {
        Player alice = player("앨리스");

        get(alice, "/api/friends/lookup/{code}", alice.code())
                .andExpect(jsonPath("$.data.relation").value("SELF"))
                .andExpect(jsonPath("$.data.player.friendCode").value(alice.code()))
                .andExpect(jsonPath("$.data.player.spiritId").doesNotExist());
    }

    @Test
    @DisplayName("차단 관계면 기존 받은 요청도 수락할 수 없다")
    void cannotAcceptAcrossBlock() throws Exception {
        Player alice = player("앨리스");
        Player bob = player("밥이");
        saveRequest(bob.id(), alice.id());
        blockRepository.save(Block.of(bob.id(), alice.id()));

        post(alice, "/api/friend-requests/{code}/accept", bob.code())
                .andExpect(status().isNotFound())
                .andExpect(jsonPath("$.code").value("F007"));
    }
}
